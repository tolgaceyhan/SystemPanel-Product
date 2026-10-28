// LAPS parolası yalnızca kullanıcı açıkça istediğinde, POST + antiforgery (csrf.js) ile alınır.
// Her gösterim sunucuda audit'lenir. Parola 30 saniye sonra ekrandan silinir ve hiçbir yerde saklanmaz.
(function () {
    var HIDE_AFTER_MS = 30000;
    var timers = new WeakMap();

    async function reveal(computerName, valueEl, infoEl) {
        if (!computerName) return;
        if (!confirm(computerName + ' için LAPS parolası gösterilsin mi? Bu işlem kayıt altına alınır.')) return;

        valueEl.textContent = '…';
        if (infoEl) infoEl.textContent = '';

        try {
            var body = new URLSearchParams({ name: computerName });
            var r = await fetch('/ad-computers/laps', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: body
            });
            var data = await r.json().catch(function () { return {}; });

            if (!r.ok || !data.password) {
                valueEl.textContent = '';
                if (infoEl) infoEl.textContent = data.message || ('LAPS okunamadı (' + r.status + ')');
                return;
            }

            valueEl.textContent = data.password;
            if (infoEl) {
                var parts = [];
                if (data.source) parts.push('Kaynak: ' + data.source);
                if (data.accountName) parts.push('Hesap: ' + data.accountName);
                if (data.expiresUtc) parts.push('Geçerlilik: ' + new Date(data.expiresUtc).toLocaleString());
                parts.push('30 sn sonra gizlenecek');
                infoEl.textContent = parts.join(' · ');
            }

            clearTimeout(timers.get(valueEl));
            timers.set(valueEl, setTimeout(function () { hide(valueEl, infoEl); }, HIDE_AFTER_MS));
        } catch (e) {
            valueEl.textContent = '';
            if (infoEl) infoEl.textContent = 'Sunucuya ulaşılamadı.';
        }
    }

    function hide(valueEl, infoEl) {
        valueEl.textContent = '';
        if (infoEl) infoEl.textContent = '';
    }

    async function copy(valueEl) {
        var text = valueEl.textContent;
        if (!text || text === '…') return;
        try { await navigator.clipboard.writeText(text); } catch (e) { /* tarayıcı izin vermeyebilir */ }
    }

    window.SystemPanelLaps = { reveal: reveal, hide: hide, copy: copy };
})();
