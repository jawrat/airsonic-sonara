import { TestBed } from '@angular/core/testing';
import { SubsonicApi } from '../core/subsonic';
import { Cover } from './cover';

describe('Cover artwork and fallback', () => {
  function setup() {
    const api = { coverArtUrl: (id?: string, size?: number, folder?: number) => id ? `/art/${id}?size=${size}&folder=${folder}` : null };
    TestBed.configureTestingModule({ imports: [Cover], providers: [{ provide: SubsonicApi, useValue: api }] });
    return TestBed.createComponent(Cover);
  }
  it('shows a colored initial tile without requesting missing artwork', () => {
    const f = setup(); f.componentRef.setInput('label', 'Grateful Dead'); f.detectChanges();
    expect(f.nativeElement.querySelector('img')).toBeNull();
    const placeholder = f.nativeElement.querySelector('.placeholder') as HTMLElement;
    expect(placeholder.textContent).toBe('GD'); expect(placeholder.style.background).not.toBe('');
    expect(f.nativeElement.getAttribute('aria-hidden')).toBe('true');
  });
  it('hides failed artwork, preserves the fallback and retries when its source changes', () => {
    const f = setup(); f.componentRef.setInput('id', 'ar-1'); f.componentRef.setInput('label', 'Grateful Dead'); f.detectChanges();
    let image = f.nativeElement.querySelector('img') as HTMLImageElement;
    image.dispatchEvent(new Event('load')); f.detectChanges();
    expect(f.nativeElement.querySelector('.placeholder').hidden).toBe(true);
    image.dispatchEvent(new Event('error')); f.detectChanges(); expect(image.hidden).toBe(true);
    expect(f.nativeElement.querySelector('.placeholder').hidden).toBe(false);
    const color = f.nativeElement.querySelector('.placeholder').style.background;
    f.componentRef.setInput('folderId', 7); f.detectChanges();
    image = f.nativeElement.querySelector('img'); expect(image.hidden).toBe(false);
    expect(image.src).toContain('folder=7'); expect(f.nativeElement.querySelector('.placeholder').style.background).toBe(color);
  });
  it('uses a music symbol when no title is available', () => {
    const f = setup(); f.detectChanges(); expect(f.nativeElement.textContent).toContain('♫');
  });
});
