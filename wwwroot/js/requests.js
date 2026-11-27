// Notifikasi & popup permintaan part untuk PC ruang sparepart (admin).
window.SPReq = (function () {
    const POLL_MS = 5000;
    const H = { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/json' };
    const esc = s => SP.esc(s), fmt = n => SP.fmt(n);
    const lvCls = { EMPTY: 'lv-empty', LOW: 'lv-low', CRITICAL: 'lv-critical', NORMAL: 'lv-normal', OVERSTOCK: 'lv-over' };
    const stCls = { New: 'st-new', Processing: 'st-proc', Ready: 'st-ready', Done: 'st-done', Rejected: 'st-rej' };
    const KEY = 'sp_req_seen';
    let modal = null, current = null, lastOpen = -1, baseTitle = document.title;

    const seen = () => { try { return +(localStorage.getItem(KEY) || 0); } catch (e) { return 0; } };
    const setSeen = id => { try { if (id > seen()) localStorage.setItem(KEY, String(id)); } catch (e) { } };

    function ago(iso) {
        const d = (Date.now() - new Date(iso).getTime()) / 60000;
        if (d < 1) return 'baru saja'; if (d < 60) return Math.floor(d) + ' menit lalu';
        if (d < 1440) return Math.floor(d / 60) + ' jam lalu';
        return new Date(iso).toLocaleString('id-ID', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
    }

    function partRow(p, qty) {
        const short = p.stockQty < qty;
        return `<div class="rq-part">
            <div class="rq-img">${p.imageUrl ? `<img src="${p.imageUrl}" alt="">` : '⚙'}</div>
            <div class="rq-info">
                <div><b>${esc(p.partNumber)}</b> <span class="badge lv ${lvCls[p.level] || ''}">${esc(p.level)}</span></div>
                <div class="rq-name">${esc(p.name)}</div>
                <div class="rq-meta"><span class="loc-chip" data-loc>📍 ${esc(p.location || 'rack belum diisi')}</span>
                    <span>stok <b>${fmt(p.stockQty)}</b> ${esc(p.unit)}</span>
                    ${short ? '<span class="text-danger fw-semibold">stok kurang</span>' : ''}</div>
                <div class="rq-msg small text-muted"></div>
            </div>
            <div class="rq-qty"><span>diminta</span><b>${fmt(qty)}</b></div>
            ${p.location ? `<button type="button" class="btn btn-sm btn-outline-primary rq-loc" data-part="${p.id}">📍 Location</button>` : ''}
        </div>`;
    }

    function render(d) {
        current = d;
        document.getElementById('reqKicker').innerHTML = `${d.urgent ? '<span class="badge bg-danger me-1">⚡ URGENT</span>' : ''}<span class="req-st ${stCls[d.status]}">${esc(d.statusText)}</span> · ${ago(d.createdAt)}`;
        document.getElementById('reqTitle').innerHTML = `${esc(d.requesterName)}${d.line ? ` <span class="text-muted fw-normal">· ${esc(d.line)}</span>` : ''} <span class="req-no">${esc(d.reqNo)}</span>`;
        const lines = d.lines.map(l => {
            if (l.part) return `<div class="rq-line">${partRow(l.part, l.qty)}</div>`;
            const head = `<div class="rq-free"><span class="rq-quote">“${esc(l.freeText)}”</span><span class="rq-qty-inline">× ${fmt(l.qty)}</span></div>`;
            if (!l.matches.length) return `<div class="rq-line">${head}
                <div class="rq-none">Tidak ditemukan di database. <a class="btn btn-sm btn-outline-secondary ms-2" href="/Search?q=${encodeURIComponent(l.freeText)}" target="_blank">⌕ Cari manual</a></div></div>`;
            return `<div class="rq-line">${head}
                <div class="rq-sub">Kemungkinan cocok — pilih part yang sesuai:</div>
                <div class="rq-grid">${l.matches.map(m => `<div class="rq-card">
                    <div class="rq-img sm">${m.imageUrl ? `<img src="${m.imageUrl}" alt="">` : '⚙'}</div>
                    <div class="rq-info"><b>${esc(m.partNumber)}</b><div class="rq-name">${esc(m.name)}</div>
                    <div class="rq-meta">📍 ${esc(m.location || '-')} · stok <b>${fmt(m.stockQty)}</b> <span class="badge lv ${lvCls[m.level] || ''}">${esc(m.level)}</span></div></div>
                    ${d.status === 'Done' || d.status === 'Rejected' ? '' : `<button type="button" class="btn btn-sm btn-primary rq-pick" data-line="${l.id}" data-part="${m.id}">Pilih</button>`}
                </div>`).join('')}</div>
                <a class="small" href="/Search?q=${encodeURIComponent(l.freeText)}" target="_blank">Bukan ini? Cari manual →</a></div>`;
        }).join('');
        document.getElementById('reqBody').innerHTML =
            (d.notes ? `<div class="rq-notes">📝 ${esc(d.notes)}</div>` : '') + lines +
            (d.responseNote ? `<div class="rq-notes mt-2">💬 ${esc(d.responseNote)}</div>` : '') +
            (d.issueDocNo ? `<div class="alert alert-success py-2 mt-2 mb-0">Sudah di-issue: <b>${esc(d.issueDocNo)}</b>${d.handledBy ? ' oleh ' + esc(d.handledBy) : ''}</div>` : '') +
            `<div class="rq-reject d-none mt-3"><label class="form-label small">Alasan ditolak (tampil di tablet peminta)</label>
                <div class="input-group"><input class="form-control" id="rqReason" maxlength="300"><button class="btn btn-danger" id="rqRejectGo">Tolak permintaan</button></div></div>`;
        const closed = d.status === 'Done' || d.status === 'Rejected';
        const unlinked = d.lines.filter(l => !l.part).length;
        document.getElementById('reqFoot').innerHTML = closed
            ? `<a class="btn btn-light me-auto" href="/Requests">Semua permintaan</a><button class="btn btn-secondary" data-bs-dismiss="modal">Tutup</button>`
            : `<a class="btn btn-light me-auto" href="/Requests">Semua permintaan</a>
               <button class="btn btn-outline-danger rq-act" data-act="reject">Tolak</button>
               ${d.status === 'New' ? '<button class="btn btn-outline-primary rq-act" data-act="Processing">Proses</button>' : ''}
               ${d.status !== 'Ready' ? '<button class="btn btn-outline-success rq-act" data-act="Ready">✓ Siap diambil</button>' : ''}
               <a class="btn btn-primary" href="/Entry/issue?request=${d.id}" title="${unlinked ? unlinked + ' baris teks belum dipilih part-nya' : ''}">Buat Issued →</a>`;
    }

    async function open(id) {
        const r = await fetch('/api/requests/' + id);
        if (!r.ok) return;
        render(await r.json());
        setSeen(id);
        const el = document.getElementById('reqModal');
        modal = modal || new bootstrap.Modal(el);
        if (!el.classList.contains('show')) modal.show();
    }

    async function post(url, body) {
        const r = await fetch(url, { method: 'POST', headers: H, body: JSON.stringify(body) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.error || 'Gagal menyimpan.'); return false; }
        return true;
    }

    document.addEventListener('click', async e => {
        const body = e.target.closest('#reqModal'); if (!body || !current) return;
        const pick = e.target.closest('.rq-pick');
        if (pick && await post(`/api/requests/${current.id}/lines/${pick.dataset.line}/link`, { partId: +pick.dataset.part })) open(current.id);
        const loc = e.target.closest('.rq-loc');
        if (loc) { const row = loc.closest('.rq-part'); SP.locate(loc.dataset.part, row.querySelector('[data-loc]'), row.querySelector('.rq-msg')); }
        const act = e.target.closest('.rq-act');
        if (act) {
            if (act.dataset.act === 'reject') { document.querySelector('#reqBody .rq-reject').classList.remove('d-none'); document.getElementById('rqReason').focus(); return; }
            if (await post(`/api/requests/${current.id}/status`, { status: act.dataset.act })) { await open(current.id); poll(); }
        }
        if (e.target.id === 'rqRejectGo') {
            if (await post(`/api/requests/${current.id}/status`, { status: 'Rejected', note: document.getElementById('rqReason').value })) { await open(current.id); poll(); }
        }
    });

    function toast(html) {
        const t = document.getElementById('reqToast'); if (!t) return;
        t.innerHTML = html; t.classList.add('show');
        clearTimeout(t._h); t._h = setTimeout(() => t.classList.remove('show'), 8000);
    }

    async function poll() {
        let j;
        try { const r = await fetch('/api/requests/poll', { cache: 'no-store' }); if (!r.ok) return; j = await r.json(); } catch (e) { return; }
        const badge = document.getElementById('reqBadge');
        if (badge) { badge.textContent = j.openCount; badge.classList.toggle('d-none', j.openCount === 0); badge.classList.toggle('pulse', j.newCount > 0); }
        document.title = (j.newCount > 0 ? `(${j.newCount}) ` : '') + baseTitle;
        document.dispatchEvent(new CustomEvent('sp:requests', { detail: j }));
        const fresh = j.items.filter(x => x.status === 'New' && x.id > seen());
        if (!fresh.length) return;
        const newest = fresh[0];
        const played = SPSound.play('message');
        if ('Notification' in window && Notification.permission === 'granted' && document.hidden) {
            try { new Notification('Permintaan part ' + newest.reqNo, { body: `${newest.requesterName}${newest.line ? ' · ' + newest.line : ''} · ${newest.items} item` }); } catch (e) { }
        }
        if (!played && !SPSound.isUnlocked()) toast('🔔 Klik di mana saja untuk mengaktifkan suara notifikasi.');
        if (fresh.length > 1) toast(`📥 ${fresh.length} permintaan baru — <a href="/Requests">lihat semua</a>`);
        if (lastOpen !== newest.id) { lastOpen = newest.id; open(newest.id); }
        fresh.forEach(x => setSeen(x.id));
    }

    function start() {
        if (!document.getElementById('reqModal')) return;
        poll();
        setInterval(poll, POLL_MS);
        document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
        // Notifikasi desktop (hanya di HTTPS / localhost; diminta sekali saat ada klik pertama).
        if ('Notification' in window && Notification.permission === 'default')
            document.addEventListener('click', () => { try { Notification.requestPermission(); } catch (e) { } }, { once: true });
    }
    document.addEventListener('DOMContentLoaded', start);
    return { open, poll };
})();
