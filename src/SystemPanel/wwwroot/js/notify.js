// Basit bildirim (toast) yardımcısı: harici kütüphane (toastr, CDN) gerektirmez; Bootstrap 5 toast kullanır.
// Kullanım: SpNotify.success("Kaydedildi"), SpNotify.error("Hata"), SpNotify.info("Bilgi")
(function () {
    "use strict";

    function container() {
        var el = document.getElementById("sp-toast-container");
        if (!el) {
            el = document.createElement("div");
            el.id = "sp-toast-container";
            el.className = "toast-container position-fixed top-0 end-0 p-3";
            el.style.zIndex = "1080";
            document.body.appendChild(el);
        }
        return el;
    }

    function show(message, css) {
        var toast = document.createElement("div");
        toast.className = "toast align-items-center border-0 text-white " + css;
        toast.setAttribute("role", "status");
        toast.setAttribute("aria-live", "polite");
        toast.setAttribute("aria-atomic", "true");

        var row = document.createElement("div");
        row.className = "d-flex";
        var body = document.createElement("div");
        body.className = "toast-body";
        body.textContent = message == null ? "" : String(message); // XSS: metin olarak eklenir
        var close = document.createElement("button");
        close.type = "button";
        close.className = "btn-close btn-close-white me-2 m-auto";
        close.setAttribute("data-bs-dismiss", "toast");
        close.setAttribute("aria-label", "Kapat");

        row.appendChild(body);
        row.appendChild(close);
        toast.appendChild(row);
        container().appendChild(toast);

        if (window.bootstrap && window.bootstrap.Toast) {
            var t = new window.bootstrap.Toast(toast, { delay: 5000 });
            toast.addEventListener("hidden.bs.toast", function () { toast.remove(); });
            t.show();
        } else {
            toast.classList.add("show");
            setTimeout(function () { toast.remove(); }, 5000);
        }
    }

    window.SpNotify = {
        success: function (m) { show(m, "bg-success"); },
        error: function (m) { show(m, "bg-danger"); },
        info: function (m) { show(m, "bg-primary"); },
        warning: function (m) { show(m, "bg-warning text-dark"); }
    };
})();
