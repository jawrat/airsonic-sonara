using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Media;
using Sonara.Api.Scanning;
using Sonara.Api.Uploads;
using Sonara.Core.Entities;
using Sonara.Data;
using System.Text.Json.Nodes;

namespace Sonara.Tests;

public class UploadPathTests
{
    [Theory]
    [InlineData("../song.mp3")][InlineData("C:\\song.mp3")][InlineData("file\n.mp3")][InlineData("CON.mp3")][InlineData("song.mp3.")][InlineData(" song.mp3")]
    public void Unsafe_names_are_rejected(string name) => Assert.Throws<InvalidDataException>(() => UploadPaths.FileName(name));
    [Fact]
    public void Relative_paths_and_links_cannot_escape_destination()
    {
        var root = Path.Combine(Path.GetTempPath(),"sonara-path-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(Path.Combine(root,"Artist","Album"),UploadPaths.DirectoryPath(root,"Artist/Album"));
            foreach(var path in new[]{"../outside","/absolute","A//B","A/./B","C:/Music"}) Assert.Throws<InvalidDataException>(()=>UploadPaths.DirectoryPath(root,path));
            if(!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(Path.Combine(root,"link"),Path.GetTempPath());Assert.Throws<InvalidDataException>(()=>UploadPaths.DirectoryPath(root,"link")); }
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Avatar_dimensions_are_bounded_and_non_images_are_rejected()
    {
        Assert.False(AvatarImages.ValidDimensions("<svg/>"u8.ToArray()));
        var png=new byte[25];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(png,0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16,4),5000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20,4),5000);
        Assert.False(AvatarImages.ValidDimensions(png));
        Assert.Equal(46,AvatarImages.Builtins().Length);
    }
}

[Collection("PostgreSQL integration")]
public class UploadAvatarIntegrationTests(ParityHost host):IClassFixture<ParityHost>
{
    private async Task<JsonNode> Upload(int folder,string filename,byte[] bytes,string user="listener",string subfolder="")
    {
        using var client=host.CreateClient();using var form=new MultipartFormDataContent();
        form.Add(new StringContent(folder.ToString()),"musicFolderId");form.Add(new StringContent(subfolder),"subfolder");form.Add(new ByteArrayContent(bytes),"file",filename);
        var response=await client.PostAsync(host.Url("uploadMusic",user),form);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subsonic-response"]!;
    }
    [DatabaseFact]
    public async Task Upload_requires_role_folder_access_and_opt_in_validates_audio_and_never_overwrites()
    {
        using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var track=await db.Tracks.FirstAsync(t=>t.Id==host.PublicTrack);var bytes=await File.ReadAllBytesAsync(track.Path);
        Assert.Equal(50,(await Upload(host.Folder,"new.wav",bytes))["error"]!["code"]!.GetValue<int>());
        Assert.Equal("ok",(await host.Call("updateUser","admin","&username=listener&uploadRole=true"))["status"]!.ToString());
        Assert.True((await host.Call("getUser"))["user"]!["uploadRole"]!.GetValue<bool>());
        Assert.Equal("failed",(await Upload(host.Folder,"new.wav",bytes))["status"]!.ToString());
        var folder=await db.MusicFolders.FirstAsync(f=>f.Id==host.Folder);folder.AllowUploads=true;
        var hidden=await db.MusicFolders.FirstAsync(f=>f.Id==host.HiddenFolder);hidden.AllowUploads=true;await db.SaveChangesAsync();
        var destinations=(await host.Call("getUploadFolders"))["uploadFolders"]!["folder"]!.AsArray();Assert.Single(destinations);
        Assert.Equal("failed",(await Upload(host.HiddenFolder,"new.wav",bytes))["status"]!.ToString());
        Assert.Equal("failed",(await Upload(host.Folder,"new.wav","not audio"u8.ToArray()))["status"]!.ToString());
        Assert.Equal("failed",(await Upload(host.Folder,"new.wav",bytes,subfolder:"../outside"))["status"]!.ToString());
        Assert.Equal("ok",(await Upload(host.Folder,"new.wav",bytes,subfolder:"Incoming"))["status"]!.ToString());
        var target=Path.Combine(folder.Path,"Incoming","new.wav");Assert.Equal(bytes,await File.ReadAllBytesAsync(target));
        Assert.Equal("failed",(await Upload(host.Folder,"new.wav",bytes,subfolder:"Incoming"))["status"]!.ToString());
        Assert.Equal(bytes,await File.ReadAllBytesAsync(target));
        var scanner=host.Services.GetRequiredService<IMediaScanner>();
        for(var i=0;i<200 && scanner.Status.Scanning;i++)await Task.Delay(25);
        using var verifyScope=host.Services.CreateScope();Assert.True(await verifyScope.ServiceProvider.GetRequiredService<SonaraDbContext>().Tracks.AnyAsync(t=>t.Path==target));
    }
    [DatabaseFact]
    public async Task Oversized_multipart_audio_is_rejected_before_saving()
    {
        var options=host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Sonara.Api.Options.SonaraOptions>>().Value;
        var previous=options.Uploads.MaxFileMegabytes;options.Uploads.MaxFileMegabytes=1;
        try
        {
            var result=await Upload(host.Folder,"oversize.wav",new byte[1024*1024+1],"admin");
            Assert.Equal("failed",result["status"]!.ToString());Assert.Contains("too large",result["error"]!["message"]!.ToString());
        }
        finally{options.Uploads.MaxFileMegabytes=previous;}
    }

    [DatabaseFact]
    public async Task Avatars_support_classic_selection_custom_upload_admin_edit_and_reject_cross_account_writes()
    {
        using var client=host.CreateClient();
        async Task<JsonNode> Save(string actor,string target,string id,byte[]? bytes=null)
        {
            using var form=new MultipartFormDataContent();form.Add(new StringContent(target),"username");form.Add(new StringContent(id),"avatarId");
            if(bytes!=null)form.Add(new ByteArrayContent(bytes),"file","avatar.png");
            var response=await client.PostAsync(host.Url("setAvatar",actor),form);return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subsonic-response"]!;
        }
        Assert.Equal(46,(await host.Call("getAvatars"))["avatars"]!["avatar"]!.AsArray().Count);
        Assert.Equal("ok",(await Save("listener","listener","Headphones"))["status"]!.ToString());
        var avatar=await client.GetAsync(host.Url("getAvatar",extra:"&username=listener"));Assert.Equal("image/png",avatar.Content.Headers.ContentType!.MediaType);
        Assert.Equal("failed",(await Save("listener","admin","Vinyl"))["status"]!.ToString());
        Assert.Equal("ok",(await Save("admin","listener","Vinyl"))["status"]!.ToString());
        Assert.Equal("failed",(await Save("listener","listener","../secret.key"))["status"]!.ToString());
        var source=await File.ReadAllBytesAsync(AvatarImages.BuiltinPath("Headphones")!);
        Assert.Equal("ok",(await Save("listener","listener","custom",source))["status"]!.ToString());
        var png=await client.GetByteArrayAsync(host.Url("getAvatar",extra:"&username=listener"));Assert.True(AvatarImages.ValidDimensions(png));
        Assert.Equal("failed",(await Save("listener","listener","custom","<svg/>"u8.ToArray()))["status"]!.ToString());
        Assert.Equal(png,await client.GetByteArrayAsync(host.Url("getAvatar",extra:"&username=listener")));
        Assert.Equal("ok",(await Save("listener","listener",""))["status"]!.ToString());
    }
    [DatabaseFact]
    public async Task Cleanup_removes_empty_artists_but_preserves_track_only_performers_and_disabled_folder_records()
    {
        using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var empty=new Artist{Name="Empty"};var performer=new Artist{Name="Track-only performer"};db.AddRange(empty,performer);await db.SaveChangesAsync();
        var track=await db.Tracks.FirstAsync(t=>t.Id==host.PublicTrack);track.ArtistId=performer.Id;
        var hidden=await db.MusicFolders.FirstAsync(f=>f.Id==host.HiddenFolder);hidden.Enabled=false;await db.SaveChangesAsync();
        await MediaScanner.RefreshCountsAsync(db);
        Assert.False(await db.Artists.AnyAsync(a=>a.Id==empty.Id));Assert.True(await db.Artists.AnyAsync(a=>a.Id==performer.Id));
        Assert.True(await db.Tracks.AnyAsync(t=>t.Id==host.HiddenTrack));
    }
}
