// Aynı origin'e giden POST/PUT/PATCH/DELETE isteklerine antiforgery token'ını otomatik ekler.
// Token _Layout'taki <meta name="csrf-token"> etiketinden okunur. Sunucu tarafında
// [ValidateAntiForgeryToken] "RequestVerificationToken" başlığını kabul eder.
(function () {
    var meta = document.querySelector('meta[name="csrf-token"]');
    if (!meta) return;
    var token = meta.getAttribute('content');
    var header = 'RequestVerificationToken';

    function isUnsafe(method) {
        return !/^(GET|HEAD|OPTIONS|TRACE)$/i.test(method || 'GET');
    }

    function isSameOrigin(url) {
        try { return new URL(url, window.location.href).origin === window.location.origin; }
        catch (e) { return false; }
    }

    if (window.fetch) {
        var originalFetch = window.fetch;
        window.fetch = function (input, init) {
            init = init || {};
            var method = init.method || (input && input.method) || 'GET';
            var url = typeof input === 'string' ? input : (input && input.url);
            if (isUnsafe(method) && isSameOrigin(url)) {
                var headers = new Headers(init.headers || (input && input.headers) || {});
                if (!headers.has(header)) headers.set(header, token);
                init.headers = headers;
            }
            return originalFetch.call(this, input, init);
        };
    }

    if (window.jQuery) {
        window.jQuery.ajaxPrefilter(function (options, originalOptions, xhr) {
            if (isUnsafe(options.type) && isSameOrigin(options.url)) {
                xhr.setRequestHeader(header, token);
            }
        });
    }
})();
