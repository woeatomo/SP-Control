// Helper global: scan QR lewat kamera + lookup part.
window.SP = (function () {
    let reader = null, modal = null, onResult = null;

    async function stop() {
        if (reader) {
            try { await reader.stop(); } catch (e) { /* belum jalan */ }
            try { reader.clear(); } catch (e) { }
            reader = null;
        }
    }

    function scan(callback) {
        onResult = callback;
        const el = document.getElementById('scanModal');
        const msg = document.getElementById('scanMsg');
        if (!modal) {
            modal = new bootstrap.Modal(el);
            el.addEventListener('hidden.bs.modal', stop);
            el.addEventListener('shown.bs.modal', start);
        }
        msg.textContent = 'Arahkan kamera ke QR code part.';
        modal.show();

        function start() {
            if (reader) return;
            if (!window.Html5Qrcode) { msg.textContent = 'Library scanner tidak termuat.'; return; }
            reader = new Html5Qrcode('qrReader');
            reader.start({ facingMode: 'environment' }, { fps: 10, qrbox: { width: 230, height: 230 } },
                async (text) => {
                    await stop();
                    modal.hide();
                    if (onResult) onResult(text.trim());
                }, () => { })
                .catch(() => {
                    msg.innerHTML = '<span class="text-danger">Kamera tidak bisa diakses.</span> Browser hanya mengizinkan kamera di <b>HTTPS</b> atau <b>localhost</b>. Scanner USB/Bluetooth tetap bisa dipakai langsung di kolom scan.';
                    reader = null;
                });
        }
    }

    async function lookup(code) {
        const r = await fetch('/api/parts/lookup?code=' + encodeURIComponent(code));
        return r.ok ? r.json() : null;
    }
    async function search(q) {
        const r = await fetch('/api/parts/search?q=' + encodeURIComponent(q));
        return r.ok ? r.json() : [];
    }
    function fmt(n) { return Number(n).toLocaleString('en-US', { maximumFractionDigits: 2 }); }
    function esc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    // Tombol "Location": minta rak memberi sinyal (lampu berkedip / LCD). Sampai perangkat rak terhubung,
    // posisi hanya ditandai berkedip di layar.
    async function locate(id, chipEl, msgEl) {
        if (chipEl) { chipEl.classList.remove('blink'); void chipEl.offsetWidth; chipEl.classList.add('blink'); }
        let j = null;
        try {
            const r = await fetch('/api/parts/' + id + '/locate', { method: 'POST', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            j = r.ok ? await r.json() : null;
        } catch (e) { }
        if (!msgEl) return;
        if (!j) msgEl.innerHTML = '<span class="text-danger">Gagal mengirim permintaan lokasi.</span>';
        else if (!j.location) msgEl.textContent = j.message;
        else msgEl.innerHTML = j.connected
            ? '<span class="text-success">✓ Sinyal dikirim ke rak <b>' + esc(j.location) + '</b>.</span>'
            : 'Posisi: <b>' + esc(j.location) + '</b>. ' + esc(j.message);
    }

    // Popup detail spare part.
    let partModal = null;
    async function showPart(id) {
        const el = document.getElementById('partModal');
        if (!el) { location.href = '/Parts/Details/' + id; return; }
        const r = await fetch('/api/parts/' + id);
        if (!r.ok) return;
        const p = await r.json(), admin = el.dataset.admin === '1';
        const row = (k, v) => `<dt class="col-5 text-muted fw-normal">${k}</dt><dd class="col-7">${v || '-'}</dd>`;
        const money = n => Number(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const lvCls = { EMPTY: 'lv-empty', LOW: 'lv-low', CRITICAL: 'lv-critical', NORMAL: 'lv-normal', OVERSTOCK: 'lv-over' };
        const state = `<span class="badge lv ${lvCls[p.level] || ''}">${esc(p.level)}</span>`
            + (['EMPTY', 'LOW', 'CRITICAL'].includes(p.level) && p.suggestedOrder > 0 ? `<div class="small text-muted">saran order ${fmt(p.suggestedOrder)}</div>` : '');
        document.getElementById('pmTitle').innerHTML = `${esc(p.partNumber)} <span class="text-muted fw-normal">· ${esc(p.name)}</span>`;
        document.getElementById('pmBody').innerHTML = `
            <div class="row g-3">
                <div class="col-md-4"><div class="part-img">${p.imageUrl
                    ? `<a href="${p.imageUrl}" target="_blank" title="Buka gambar ukuran penuh"><img src="${p.imageUrl}" alt="Gambar part"></a>`
                    : `<div class="text-center text-muted small">Belum ada gambar${admin ? `<div><a href="/Parts/Edit/${p.id}">Upload gambar</a></div>` : ''}</div>`}</div></div>
                <div class="col-md-5"><dl class="row mb-0">
                    ${row('Spesifikasi', esc(p.specification))}${row('Model', esc(p.category))}${row('Maker', esc(p.brand))}
                    ${row('Supplier', esc(p.supplier))}${row('Mesin / Line', esc(p.machine))}
                    ${el.dataset.prices === '1' ? row('Harga', esc(p.currency) + ' ' + fmt(p.unitPrice) + ' <span class="text-muted">(USD ' + money(p.unitPriceUsd) + ')</span>') : ''}
                    ${el.dataset.prices === '1' ? row('Nilai Stok', '<b>USD ' + money(p.totalValueUsd) + '</b>') : ''}
                    ${row('Tanda', (p.condition === 'Second' ? '<span class="badge bg-secondary">2nd used</span> ' : 'Baru ') + (p.isReusable ? '<span class="badge badge-soft">♻ Reusable</span>' : ''))}
                    ${row('Stock Category', p.status === 'Active' ? 'ACTIVE' : 'INACTIVE')}${row('Remark', esc(p.remarks))}
                </dl></div>
                <div class="col-md-3 text-center">
                    <img src="/qr?c=${encodeURIComponent(p.partNumber)}&s=5" alt="QR" style="width:120px;height:120px">
                    <div class="stat p-0 mt-2"><div class="label">Stok saat ini</div>
                        <div class="value">${fmt(p.stockQty)} <span class="fs-6 fw-normal text-muted">${esc(p.unit)}</span></div>
                        <div class="sub">Min ${fmt(p.minStock)} · Max ${fmt(p.maxStock)}</div>${state}</div>
                </div>
                <div class="col-12"><div class="border rounded p-2 d-flex flex-wrap align-items-center gap-2">
                    <span class="text-muted">Lokasi penyimpanan:</span>
                    <span class="loc-chip fs-6" id="pmLoc">📍 ${esc(p.location) || 'belum diisi'}</span>
                    ${admin ? '<button type="button" class="btn btn-primary btn-sm" id="pmLocate">📍 Location</button>' : ''}
                    <span class="small text-muted" id="pmLocMsg"></span>
                </div></div>
            </div>`;
        document.getElementById('pmFoot').innerHTML =
            (admin ? `<a class="btn btn-primary btn-sm" href="/Entry/issue?partId=${p.id}">Issue</a>
                      <a class="btn btn-outline-secondary btn-sm" href="/Parts/Edit/${p.id}">Edit</a>` : '') +
            `<a class="btn btn-outline-secondary btn-sm" href="/Parts/Labels?ids=${p.id}" target="_blank">🏷 Label QR</a>
             <a class="btn btn-outline-secondary btn-sm" href="/Parts/Details/${p.id}">Detail lengkap &amp; kartu stok</a>`;
        document.getElementById('pmLocate')?.addEventListener('click', () =>
            locate(p.id, document.getElementById('pmLoc'), document.getElementById('pmLocMsg')));
        partModal = partModal || new bootstrap.Modal(el);
        partModal.show();
    }

    return { scan, lookup, search, fmt, esc, locate, showPart };
})();


// ---- Kotak cari di menu atas: hasil langsung saat mengetik ----
(function () {
    const box = document.getElementById('navSearch'), sugg = document.getElementById('navSugg');
    if (!box || !sugg) return;
    let timer, seq = 0, items = [], active = -1;
    const lvCls = { EMPTY: 'lv-empty', LOW: 'lv-low', CRITICAL: 'lv-critical', NORMAL: 'lv-normal', OVERSTOCK: 'lv-over' };
    const close = () => { sugg.classList.remove('show'); active = -1; };
    function render(total, q) {
        if (!items.length) { sugg.innerHTML = `<div class="ns-empty">Tidak ada part yang cocok.</div>`; sugg.classList.add('show'); return; }
        sugg.innerHTML = items.map((p, i) => `<a href="/Parts/Details/${p.id}" class="ns-item ${i === active ? 'active' : ''}" data-i="${i}" role="option">
                <div class="ns-line"><b>${SP.esc(p.partNumber)}</b><span class="badge lv ${lvCls[p.level] || ''}">${SP.esc(p.level)}</span></div>
                <div class="ns-sub">${SP.esc(p.name)}</div>
                <div class="ns-sub">📍 ${SP.esc(p.location || '-')} · stok <b>${SP.fmt(p.stockQty)}</b> ${SP.esc(p.unit)}</div>
            </a>`).join('') +
            `<a class="ns-all" href="/Search?q=${encodeURIComponent(q)}">Lihat semua hasil${total > items.length ? ' (' + total + ')' : ''} →</a>`;
        sugg.classList.add('show');
    }
    async function run() {
        const q = box.value.trim();
        if (q.length < 2) { close(); return; }
        const my = ++seq;
        const r = await fetch('/api/parts/find?take=6&q=' + encodeURIComponent(q));
        if (my !== seq) return;
        const j = r.ok ? await r.json() : { total: 0, items: [] };
        items = j.items; active = -1; render(j.total, q);
    }
    box.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(run, 180); });
    box.addEventListener('focus', () => { if (box.value.trim().length >= 2) run(); });
    box.addEventListener('keydown', e => {
        if (!sugg.classList.contains('show')) return;
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            active = (active + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
            render(items.length, box.value.trim());
        } else if (e.key === 'Enter' && active >= 0) {
            e.preventDefault(); const p = items[active]; close(); SP.showPart(p.id);
        } else if (e.key === 'Escape') { close(); box.blur(); }
    });
    sugg.addEventListener('mousedown', e => e.preventDefault()); // jangan hilang sebelum klik
    sugg.addEventListener('click', e => {
        if (e.target.closest('.ns-all')) { e.preventDefault(); location.href = '/Search?q=' + encodeURIComponent(box.value.trim()); return; }
        const a = e.target.closest('.ns-item'); if (!a || e.ctrlKey || e.metaKey || e.shiftKey) return;
        e.preventDefault(); close(); SP.showPart(items[+a.dataset.i].id);
    });
    box.addEventListener('blur', () => setTimeout(close, 150));
    // Tombol "/" untuk langsung mencari (kecuali sedang mengetik di kolom lain).
    document.addEventListener('keydown', e => {
        if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
        const t = e.target; if (t.matches('input, textarea, select, [contenteditable]')) return;
        e.preventDefault(); (document.getElementById('sq') || box).focus();
    });
})();
