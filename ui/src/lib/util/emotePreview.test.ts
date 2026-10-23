import { describe, expect, it } from 'vitest';
import { pose, render } from './emotePreview';

const anim = JSON.stringify({
  animations: {
    wave: {
      bones: {
        right_arm: { rotation: { '0.0': { vector: [0, 0, 0] }, '0.3': { vector: [-165, 0, 0] }, '1.8': { vector: [0, 0, 0] } } },
        Head: { rotation: [5, 0, 0] }
      }
    }
  }
});

describe('Emote-Vorschau', () => {
  it('nimmt das Schlüsselbild mit der stärksten Auslenkung', () => {
    const p = pose(anim, 'wave');
    expect(p.rotation.right_arm).toEqual([-165, 0, 0]);
    expect(p.rotation.head).toEqual([5, 0, 0]);
  });

  it('ergibt bei unbekannten Daten die Grundhaltung', () => {
    expect(pose('{"emote": {}}', null)).toEqual({ rotation: {}, position: {} });
    expect(pose('kein json', null)).toEqual({ rotation: {}, position: {} });
  });

  it('zeichnet einen Skin in die Mitte des Bildes', () => {
    const skin = Array.from({ length: 64 }, () => Array.from({ length: 64 }, () => 0xff808080));
    const out = render(skin, pose(anim, 'wave'), 32);
    expect(out.length).toBe(32 * 32);
    expect(out[16 * 32 + 16]).not.toBe(0);
    expect(out[0]).toBe(0);
  });
});
