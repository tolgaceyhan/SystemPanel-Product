// wwwroot/js/adcomputers.js
// AD Bilgisayar listesi – AJAX + arama + sıralama + sayfalama

(function () {
    let currentPage = 1;
    let pageSize = 50;
    let currentSortColumn = "Name";
    let currentSortDir = "asc";
    let currentSearchTerm = "";
    let isLoading = false;

    function init() {
        bindEvents();
        loadPage(1);
    }

    function bindEvents() {
        const $search = $("#computer-search");
        let searchTimeout = null;

        $search.on("keyup", function () {
            const val = $(this).val() || "";
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(function () {
                currentSearchTerm = val;
                loadPage(1);
            }, 400);
        });

        // Sıralama: kolon başlıkları
        $("#computers-table").on("click", "th.sortable", function () {
            const col = $(this).data("sort");
            if (!col) return;

            if (currentSortColumn === col) {
                currentSortDir = currentSortDir === "asc" ? "desc" : "asc";
            } else {
                currentSortColumn = col;
                currentSortDir = "asc";
            }

            updateSortIndicators();
            loadPage(currentPage);
        });

        // Paging
        $("#computers-pager").on("click", "a.page-link[data-page]", function (e) {
            e.preventDefault();
            const page = parseInt($(this).data("page"), 10);
            if (!isNaN(page) && page !== currentPage) {
                loadPage(page);
            }
        });

        // Detay butonu
        $("#computers-body").on("click", ".btn-detail", function () {
            const name = $(this).data("name");
            if (name) {
                openDetailModal(name);
            }
        });

        // LAPS göster (istek üzerine)
        $("#btn-reveal-laps").on("click", function () {
            SystemPanelLaps.reveal($(this).data("name"), document.getElementById("detail-laps"), document.getElementById("detail-laps-info"));
        });

        // Modal kapanınca parolayı ekrandan sil
        $("#computerDetailModal").on("hidden.bs.modal", function () {
            SystemPanelLaps.hide(document.getElementById("detail-laps"), document.getElementById("detail-laps-info"));
        });

        // LAPS kopyala
        $("#btn-copy-laps").on("click", function () {
            const text = $("#detail-laps").text();
            if (!text) return;

            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text)
                    .then(() => {
                        showToast("LAPS parolası kopyalandı.");
                    })
                    .catch(() => {
                        alert("Kopyalama başarısız oldu.");
                    });
            } else {
                // Eski browser fallback
                const tmp = $("<input>");
                $("body").append(tmp);
                tmp.val(text).select();
                document.execCommand("copy");
                tmp.remove();
                showToast("LAPS parolası kopyalandı.");
            }
        });

        updateSortIndicators();
    }

    function showLoading(show) {
        isLoading = show;
        $("#table-loading").toggleClass("d-none", !show);
        if (show) {
            $("#computers-body").html(`
                <tr class="table-active">
                    <td colspan="6" class="text-center text-muted py-4">
                        Liste yükleniyor...
                    </td>
                </tr>`);
        }
    }

    function loadPage(page) {
        if (isLoading) return;

        currentPage = page;
        showLoading(true);

        $.ajax({
            url: "/ad-computers/list",
            method: "GET",
            data: {
                searchTerm: currentSearchTerm,
                page: currentPage,
                pageSize: pageSize,
                sortColumn: currentSortColumn,
                sortDir: currentSortDir
            },
            success: function (res) {
                renderTable(res.items || []);
                renderPager(res.total || 0, res.page || 1, res.pageSize || pageSize);
                updateSummary(res.total || 0, res.page || 1, res.pageSize || pageSize);
            },
            error: function () {
                $("#computers-body").html(`
                    <tr class="table-danger">
                        <td colspan="6" class="text-center py-4">
                            <strong>Hata:</strong> Bilgisayar listesi yüklenirken bir hata oluştu.
                        </td>
                    </tr>`);
                updateSummary(0, 1, pageSize);
            },
            complete: function () {
                showLoading(false);
            }
        });
    }

    function renderTable(items) {
        if (!items || items.length === 0) {
            $("#computers-body").html(`
                <tr>
                    <td colspan="6" class="text-center text-muted py-4">
                        Kayıt bulunamadı.
                    </td>
                </tr>`);
            return;
        }

        const rows = items.map(function (c) {
            const lastLogon = c.lastLogonDate
                ? formatDateTime(c.lastLogonDate)
                : "";

            const statusBadge = c.status === "Aktif"
                ? `<span class="badge bg-success">Aktif</span>`
                : `<span class="badge bg-secondary">Pasif</span>`;

            const osText = c.operatingSystem || "";
            const descText = c.description || "";

            return `
                <tr>
                    <td>${escapeHtml(c.name || "")}</td>
                    <td class="d-none d-md-table-cell">${escapeHtml(osText)}</td>
                    <td class="d-none d-lg-table-cell">${escapeHtml(descText)}</td>
                    <td class="d-none d-md-table-cell">${escapeHtml(lastLogon)}</td>
                    <td>${statusBadge}</td>
                    <td class="text-end">
                        <button type="button"
                                class="btn btn-sm btn-outline-primary btn-detail"
                                data-name="${htmlAttr(c.name || "")}">
                            Detay
                        </button>
                    </td>
                </tr>`;
        });

        $("#computers-body").html(rows.join(""));
    }

    function renderPager(total, page, pageSize) {
        const $pager = $("#computers-pager");
        $pager.empty();

        if (total <= 0) return;

        const totalPages = Math.max(1, Math.ceil(total / pageSize));

        const makeItem = function (p, text, disabled, active) {
            return `
                <li class="page-item ${disabled ? "disabled" : ""} ${active ? "active" : ""}">
                    <a class="page-link" href="#" data-page="${p}">${text}</a>
                </li>`;
        };

        // Prev
        $pager.append(makeItem(page - 1, "&laquo;", page <= 1, false));

        const windowSize = 5;
        let start = Math.max(1, page - 2);
        let end = Math.min(totalPages, start + windowSize - 1);
        if (end - start + 1 < windowSize) {
            start = Math.max(1, end - windowSize + 1);
        }

        for (let i = start; i <= end; i++) {
            $pager.append(makeItem(i, i, false, i === page));
        }

        // Next
        $pager.append(makeItem(page + 1, "&raquo;", page >= totalPages, false));
    }

    function updateSummary(total, page, pageSize) {
        if (!total) {
            $("#table-summary").text("Toplam 0 kayıt");
            return;
        }

        const from = (page - 1) * pageSize + 1;
        const to = Math.min(total, page * pageSize);

        $("#table-summary").text(`Toplam ${total} kayıt – ${from} - ${to} arası gösteriliyor`);
    }

    function updateSortIndicators() {
        $("#computers-table th.sortable").each(function () {
            const $th = $(this);
            const col = $th.data("sort");
            const $icon = $th.find(".sort-indicator");

            $icon.text("");

            if (col === currentSortColumn) {
                if (currentSortDir === "asc")
                    $icon.html("&uarr;");
                else
                    $icon.html("&darr;");
            }
        });
    }

    function openDetailModal(name) {
        const $modal = $("#computerDetailModal");
        const $loading = $("#computer-detail-loading");
        const $content = $("#computer-detail-content");
        const $error = $("#computer-detail-error");

        $error.addClass("d-none").text("");
        $content.addClass("d-none");
        $loading.removeClass("d-none");

        const modal = bootstrap.Modal.getOrCreateInstance($modal[0]);
        modal.show();

        $.ajax({
            url: "/ad-computers/details",
            method: "GET",
            data: { name: name },
            headers: { "X-Requested-With": "XMLHttpRequest" },
            success: function (data) {
                if (!data) {
                    showDetailError("Kayıt bulunamadı.");
                    return;
                }

                $("#detail-name").text(data.name || "");
                $("#detail-dn").text(data.distinguishedName || "");
                $("#detail-os").text(data.operatingSystem || "");
                $("#detail-desc").text(data.description || "");
                $("#detail-lastlogon").text(data.lastLogonDate ? formatDateTime(data.lastLogonDate) : "");
                $("#detail-status").text(data.status || "");
                // LAPS parolası detay yanıtında GELMEZ; "Göster" ile ayrıca (audit'li) istenir.
                $("#detail-laps").text("");
                $("#detail-laps-info").text("");
                $("#btn-reveal-laps").data("name", data.name || "");

                $loading.addClass("d-none");
                $content.removeClass("d-none");
            },
            error: function (xhr) {
                if (xhr.status === 403) {
                    showDetailError("Bu bilgisayar için yetkiniz yok.");
                } else if (xhr.status === 404) {
                    showDetailError("Bilgisayar bulunamadı.");
                } else {
                    showDetailError("Detaylar alınırken bir hata oluştu.");
                }
            }
        });

        function showDetailError(msg) {
            $loading.addClass("d-none");
            $content.addClass("d-none");
            $error.text(msg).removeClass("d-none");
        }
    }

    function formatDateTime(dateStr) {
        // dateStr ISO DateTime (backend’den)
        const d = new Date(dateStr);
        if (isNaN(d.getTime())) return "";

        const pad = (n) => (n < 10 ? "0" + n : n);

        const day = pad(d.getDate());
        const month = pad(d.getMonth() + 1);
        const year = d.getFullYear();

        const hour = pad(d.getHours());
        const min = pad(d.getMinutes());

        return `${day}.${month}.${year} ${hour}:${min}`;
    }

    function escapeHtml(str) {
        if (str === null || str === undefined) return "";
        return String(str)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function htmlAttr(str) {
        if (str === null || str === undefined) return "";
        return String(str)
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function showToast(message) {
        // Basit toast – istersen Bootstrap Toast ile de upgrade edilebilir.
        if (window.Toastify) {
            Toastify({ text: message, duration: 2000 }).showToast();
        } else {
            console.log(message);
        }
    }

    // Sayfa hazır olunca başlat
    $(document).ready(init);
})();
