import { TestBed } from '@angular/core/testing';
import { SubsonicApi } from '../core/subsonic';
import { SettingsPage } from './settings';

describe('SettingsPage', () => {
  it('handles numeric input and submits settings as strings', async () => {
    const call = vi.fn(async (operation: string) => {
      if (operation === 'getSettings') return { settings: { setting: [
        { key: 'Scanner.Parallelism', label: 'Scanner parallelism', kind: 'int', value: '2', effectiveValue: '2', min: 0, max: 64 },
      ] } };
      if (operation === 'getMusicFolderSettings') return { musicFolderSettings: { folder: [] } };
      return { restartRequired: true };
    });
    TestBed.configureTestingModule({ imports: [SettingsPage], providers: [{ provide: SubsonicApi, useValue: { call } }] });
    const fixture = TestBed.createComponent(SettingsPage);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('input[type=number]')).not.toBeNull(); });
    const input = fixture.nativeElement.querySelector('input[type=number]') as HTMLInputElement;
    input.value = '3'; input.dispatchEvent(new Event('input')); fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Pending restart');
    await fixture.componentInstance.save(); fixture.detectChanges();
    expect(call).toHaveBeenCalledWith('updateSettings', { key: ['Scanner.Parallelism'], value: ['3'] });
    expect(fixture.componentInstance.message()).toContain('Restart Sonara');
  });

  it('reports API errors and releases the busy state', async () => {
    const call = vi.fn(async (operation: string) => {
      if (operation === 'getSettings') return { settings: { setting: [] } };
      if (operation === 'getMusicFolderSettings') return { musicFolderSettings: { folder: [] } };
      throw new Error('Save denied');
    });
    TestBed.configureTestingModule({ imports: [SettingsPage], providers: [{ provide: SubsonicApi, useValue: { call } }] });
    const fixture = TestBed.createComponent(SettingsPage);
    fixture.detectChanges(); await fixture.whenStable();
    await fixture.componentInstance.save(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('Save denied');
    expect(fixture.componentInstance.busy()).toBe(false);
  });
});
