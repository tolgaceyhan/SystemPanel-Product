const tooltip = document.getElementById("map-tooltip");
const mapContainer = document.querySelector(".map-container");
const svg = document.getElementById("turkey-map");

const REFRESH_MS = 30000;

/* ============ CLEANUP ============ */
function clearOverlays() {
    document
        .querySelectorAll(".map-badge, .map-alarm, .map-restart, .map-disabled")
        .forEach(e => e.remove());

    document
        .querySelectorAll(".alarm-down, .alarm-warning")
        .forEach(e => e.classList.remove("alarm-down", "alarm-warning"));
}

/* ============ BADGE ============ */
function placeBadge(el, text) {
    const bbox = el.getBBox();
    const pt = svg.createSVGPoint();
    pt.x = bbox.x + bbox.width;
    pt.y = bbox.y;

    const screen = pt.matrixTransform(svg.getScreenCTM());
    const badge = document.createElement("div");
    badge.className = "map-badge";
    badge.innerText = text;
    badge.style.left = `${screen.x}px`;
    badge.style.top = `${screen.y}px`;
    mapContainer.appendChild(badge);
}

/* ============ ICONS ============ */
function placeAlarm(el) {
    const bbox = el.getBBox();
    const pt = svg.createSVGPoint();
    pt.x = bbox.x + bbox.width;
    pt.y = bbox.y + bbox.height;

    const screen = pt.matrixTransform(svg.getScreenCTM());
    const icon = document.createElement("div");
    icon.className = "map-alarm";
    icon.innerText = "!";
    icon.style.left = `${screen.x}px`;
    icon.style.top = `${screen.y}px`;
    mapContainer.appendChild(icon);
}

function placeRestartIcon(el, provinceName) {
    const bbox = el.getBBox();
    const pt = svg.createSVGPoint();
    pt.x = bbox.x + bbox.width / 2;
    pt.y = bbox.y + bbox.height / 2;

    const screen = pt.matrixTransform(svg.getScreenCTM());
    const icon = document.createElement("div");
    icon.className = "map-restart";
    icon.innerText = "⚡";
    icon.title = "Kritik servisleri restart et";

    icon.style.left = `${screen.x}px`;
    icon.style.top = `${screen.y}px`;

    icon.onclick = e => {
        e.stopPropagation();
        restartProvince(provinceName);
    };

    mapContainer.appendChild(icon);
}

function placeDisabledIcon(el) {
    const bbox = el.getBBox();
    const pt = svg.createSVGPoint();
    pt.x = bbox.x + bbox.width / 2;
    pt.y = bbox.y + bbox.height;

    const screen = pt.matrixTransform(svg.getScreenCTM());
    const icon = document.createElement("div");
    icon.className = "map-disabled";
    icon.innerText = "🚫";
    icon.title = "Auto-Heal kapalı";

    icon.style.left = `${screen.x}px`;
    icon.style.top = `${screen.y}px`;

    mapContainer.appendChild(icon);
}

/* ============ APPLY DATA ============ */
function applyData(data) {
    clearOverlays();

    data.forEach(p => {

        const el = document.getElementById(p.province);
        if (!el) {
            console.warn("SVG bulunamadı:", p.province);
            return;
        }

        let color = "#28a745";
        if (p.status === "Warning") color = "#ffc107";
        if (p.status === "Down") color = "#dc3545";
        if (p.status === "Unknown") color = "#adb5bd"; // veri yok: sağlıklı varsayılmaz

        el.querySelector("path")?.style.fill = color;
        el.style.cursor = "pointer";

        if (p.problemDp > 0) {
            placeBadge(el, p.problemDp);
        }

        if (p.autoHealDisabled) {
            placeDisabledIcon(el);
        }
        else if (p.status === "Down") {
            el.classList.add("alarm-down");
            placeAlarm(el);
            placeRestartIcon(el, p.provinceName);
        }
        else if (p.status === "Warning") {
            el.classList.add("alarm-warning");
            placeRestartIcon(el, p.provinceName);
        }

        const lastHeal =
            p.lastHealUtc
                ? new Date(p.lastHealUtc).toLocaleString()
                : "—";

        el.onmousemove = e => {
            tooltip.innerHTML = `
                <div class="title">${p.provinceName}</div>
                <div>Toplam DP: <b>${p.totalDp}</b></div>
                <div>Problemli DP: <b>${p.problemDp}</b></div>
                <div>Son Auto-Heal: <b>${lastHeal}</b></div>
                <div class="${p.status === "Healthy" ? "ok" : p.status === "Down" ? "bad" : "warn"}">
                    ${p.autoHealDisabled
                    ? "⛔ Auto-Heal Kapalı"
                    : p.status === "Down"
                        ? "ACİL – İl Down"
                        : p.status === "Warning"
                            ? "Uyarı – Kısmi Sorun"
                            : p.status === "Unknown"
                                ? "Veri yok – durum bilinmiyor"
                                : "Sağlıklı"}
                </div>
            `;
            tooltip.style.left = e.pageX + 15 + "px";
            tooltip.style.top = e.pageY + 15 + "px";
            tooltip.style.display = "block";
        };

        el.onmouseleave = () => tooltip.style.display = "none";

        el.onclick = () => {
            location.href = `/MapUnits/ProvinceDetails?provinceName=${p.provinceName}`;
        };
    });
}

/* ============ REFRESH ============ */
async function refreshMap() {
    try {
        const res = await fetch("/MapUnits/ProvinceStatus", { cache: "no-store" });
        const data = await res.json();
        applyData(data);
    } catch (e) {
        console.error("Map refresh failed", e);
    }
}

refreshMap();
setInterval(refreshMap, REFRESH_MS);

/* ============ RESTART ============ */
function restartProvince(provinceName) {
    if (!confirm(`${provinceName} için TÜM problemli DP servisleri yeniden başlatılsın mı?`))
        return;

    fetch("/AutoHeal/RestartCriticalDp", {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: `provinceName=${provinceName}`
    })
        .then(r => r.json())
        .then(res => {
            alert(res.success ? "Restart tetiklendi" : "Restart başarısız");
            if (res.success) setTimeout(refreshMap, 3000);
        })
        .catch(() => alert("Restart işlemi başarısız"));
}
