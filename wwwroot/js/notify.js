// Suara notifikasi (disintesis dengan Web Audio, tanpa file audio).
// Browser hanya mengizinkan suara setelah ada interaksi (klik/tap) di halaman; unlock() dipanggil otomatis.
window.SPSound = (function () {
    let ctx = null, unlocked = false;
    const KEY = 'sp_sound_on';
    const enabled = () => { try { return localStorage.getItem(KEY) !== '0'; } catch (e) { return true; } };
    function setEnabled(on) { try { localStorage.setItem(KEY, on ? '1' : '0'); } catch (e) { } }
    function ac() {
        if (!ctx) { const C = window.AudioContext || window.webkitAudioContext; if (!C) return null; ctx = new C(); }
        return ctx;
    }
    function unlock() {
        const c = ac(); if (!c) return;
        if (c.state === 'suspended') c.resume();
        unlocked = c.state !== 'suspended' || unlocked;
        if (!unlocked) c.resume().then(() => { unlocked = true; });
    }
    ['pointerdown', 'keydown', 'touchstart'].forEach(ev => document.addEventListener(ev, unlock, { passive: true }));

    function tone(c, freq, start, dur, vol) {
        const o = c.createOscillator(), g = c.createGain();
        o.type = 'sine'; o.frequency.setValueAtTime(freq, start);
        // Sedikit harmonik supaya terdengar seperti "ding" lembut.
        const o2 = c.createOscillator(), g2 = c.createGain();
        o2.type = 'sine'; o2.frequency.setValueAtTime(freq * 2, start);
        g.gain.setValueAtTime(0.0001, start);
        g.gain.exponentialRampToValueAtTime(vol, start + 0.012);
        g.gain.exponentialRampToValueAtTime(0.0001, start + dur);
        g2.gain.setValueAtTime(0.0001, start);
        g2.gain.exponentialRampToValueAtTime(vol * 0.18, start + 0.012);
        g2.gain.exponentialRampToValueAtTime(0.0001, start + dur * 0.6);
        o.connect(g).connect(c.destination); o2.connect(g2).connect(c.destination);
        o.start(start); o2.start(start); o.stop(start + dur + 0.05); o2.stop(start + dur + 0.05);
    }

    /** kind: 'message' (permintaan masuk: tiga nada naik) | 'ready' (dua nada) */
    function play(kind) {
        if (!enabled()) return false;
        const c = ac(); if (!c) return false;
        if (c.state === 'suspended') { c.resume(); if (!unlocked) return false; }
        const t = c.currentTime + 0.02;
        if (kind === 'ready') { tone(c, 880, t, 0.35, 0.22); tone(c, 1318.5, t + 0.16, 0.5, 0.2); }
        else { tone(c, 1174.7, t, 0.28, 0.2); tone(c, 1568, t + 0.13, 0.28, 0.2); tone(c, 2093, t + 0.26, 0.55, 0.18); }
        return true;
    }
    return { play, unlock, enabled, setEnabled, isUnlocked: () => unlocked };
})();
