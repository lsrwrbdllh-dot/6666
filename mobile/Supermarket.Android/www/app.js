"use strict";
const $ = (s) => document.querySelector(s);
const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const paths = {
  home: "m3 10 9-7 9 7v10H3z M9 20v-7h6v7",
  cube: "m12 3 9 5-9 5-9-5z M3 8v10l9 5 9-5V8 M12 13v10",
  chart: "M4 20V10h3v10 M10 20V4h3v16 M16 20V7h3v13",
  receipt: "M5 3h14v19l-3-2-4 2-4-2-3 2z M9 8h6 M9 12h6 M9 16h4",
  wallet: "M3 6h17v14H3z M3 6l13-3v3 M16 11h5v5h-5z",
  plus: "M12 5v14 M5 12h14",
  search: "M10.5 18a7.5 7.5 0 1 0 0-15 7.5 7.5 0 0 0 0 15 M16 16l5 5",
  more: "M4 12h.1 M12 12h.1 M20 12h.1",
  arrow: "m15 5-7 7 7 7",
  close: "m5 5 14 14 M19 5 5 19",
  cart: "M2 3h3l3 13h11l3-10H6 M10 21h.1 M18 21h.1",
  users:
    "M8 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M1 22v-3a7 7 0 0 1 14 0v3 M17 5a4 4 0 0 1 0 7 M17 16a6 6 0 0 1 6 6",
  settings:
    "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M9 2h6l1 4 4 1 2 5-3 3v4l-5 3-3-2-4-1-3-5 2-3V7l3-2z",
  shield: "m12 2 9 4v7c0 5-9 9-9 9S3 18 3 13V6z m-4 10 3 3 5-6",
  calendar: "M4 5h16v16H4z M8 2v6 M16 2v6 M4 11h16",
  clock: "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M12 7v6l4 2",
  download: "M12 3v12 m-5-5 5 5 5-5 M3 16v5h18v-5",
  print: "M7 8V2h10v6 M6 18H2V8h20v10h-4 M6 14h12v9H6z M18 11h1",
  camera: "M3 6h5l2-3h4l2 3h5v15H3z M12 9a4 4 0 1 0 0 8 4 4 0 0 0 0-8",
  book: "M12 5C8 2 4 3 2 4v16c4-2 7-1 10 1 3-2 6-3 10-1V4c-4-2-7-1-10 1z M12 5v16",
  bell: "M5 17h14l-2-3V9a5 5 0 0 0-10 0v5z M10 21h4",
  logout: "M10 3H3v18h7 M8 12h14 m-5-5 5 5-5 5",
  check: "m5 12 4 4 10-11",
  tag: "M3 3h8l11 11-8 8L3 11z M7 7h.1",
  robot: "M5 7h14v13H5z M12 3v4 M8 12h.1 M16 12h.1 M9 16h6 M2 10v7 M22 10v7",
  folder: "M2 5h8l3 3h9v13H2z",
  trash: "M3 6h18 M8 6V3h8v3 M6 6l1 16h10l1-16 M10 10v8 M14 10v8",
};
const icon = (name, size = 22) =>
  `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name] || paths.receipt}"/></svg>`;
const kinds = {
  Sale: "بيع",
  Purchase: "شراء",
  Receipt: "تحصيل",
  Payment: "سداد",
  Expense: "مصروف",
  Capital: "تمويل",
  CashSurplus: "فائض صندوق",
  Adjustment: "تسوية مخزون",
};
const accounts = {
  Cash: "الصندوق",
  Inventory: "المخزون",
  Receivable: "مديونية العملاء",
  Payable: "مستحقات الموردين",
  Sales: "المبيعات",
  CostOfSales: "تكلفة المبيعات",
  Expense: "المصروفات",
  Capital: "رأس المال",
  CashSurplus: "فائض الصندوق",
  InventorySurplus: "فائض الجرد",
};
let serial = 0,
  pending = new Map(),
  busy = 0,
  S = {},
  settings = {},
  route = "home",
  data = {},
  invoice = null,
  context = {},
  confirmResolve = null,
  guideStep = -1,
  lastBack = 0,
  reportRows = [],
  reportType = "income";
let aiState = {
  info: null,
  question: "",
  answer: "",
  error: "",
  days: 30,
  running: false,
  requestId: 0,
  facts: null,
  model: "",
  stopped: false,
  epoch: 0,
};
let inventoryFilter = { search: "", warehouse: 0, category: "", low: false };
const app = $("#app"),
  dialog = $("#modal");
const currency = () => esc(settings.currency || "وحدة نقدية");
const money = (cents, unit = true) =>
  `${Number((Number(cents) || 0) / 100).toLocaleString("ar", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}${unit ? " " + currency() : ""}`;
const quantity = (q) =>
  Number((Number(q) || 0) / 1000).toLocaleString("ar", {
    maximumFractionDigits: 3,
  });
const date = (d) =>
  d
    ? new Date(d).toLocaleDateString("ar", {
        year: "numeric",
        month: "short",
        day: "numeric",
      })
    : "—";
const today = () =>
  new Date(Date.now() - new Date().getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 10);
const uuid = () =>
  crypto.randomUUID
    ? crypto.randomUUID()
    : "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === "x" ? r : (r & 3) | 8).toString(16);
      });
const num = (v) => {
  const s = String(v ?? "")
    .replace(/[٠-٩۰-۹]/g, (c) =>
      String(c.charCodeAt(0) - (c <= "٩" ? 1632 : 1776)),
    )
    .replace(/٫/g, ".")
    .replace(/٬/g, "");
  const n = Number(s);
  if (!Number.isFinite(n)) throw Error("أدخل رقماً صحيحاً");
  return n;
};
const owner = () => S.user?.role === "Owner",
  writer = () => S.user?.role !== "Viewer";
function rpc(action, args = {}) {
  if (!window.MadarNative)
    return Promise.reject(
      Error("تُفتح هذه الواجهة داخل تطبيق مدار سوفت لأندرويد."),
    );
  const id = ++serial;
  if (action === "ai.ask") aiState.requestId = id;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => {
        pending.delete(id);
        reject(Error("تأخرت العملية؛ تحقق من السجل قبل تكرار الحفظ."));
      },
      action === "ai.ask"
        ? 240000
        : action.startsWith("files.") || action === "ai.import"
          ? 600000
          : 45000,
    );
    pending.set(id, { resolve, reject, timer });
    window.MadarNative.invoke(JSON.stringify({ id, action, args }));
  });
}
window.madarReceive = (response) => {
  const request = pending.get(response.id);
  if (!request) return;
  clearTimeout(request.timer);
  pending.delete(response.id);
  response.ok
    ? request.resolve(response.data)
    : request.reject(Error(response.message || "تعذر تنفيذ العملية"));
};
function toast(message) {
  const el = $("#toast");
  el.textContent = message;
  el.classList.add("show");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => el.classList.remove("show"), 6000);
}
async function work(fn) {
  busy++;
  $("#loading").hidden = false;
  document.querySelectorAll("[data-write]").forEach((b) => (b.disabled = true));
  try {
    return await fn();
  } catch (e) {
    toast(e.message);
    return undefined;
  } finally {
    busy--;
    $("#loading").hidden = busy === 0;
    if (!busy)
      document
        .querySelectorAll("[data-write]")
        .forEach((b) => (b.disabled = false));
  }
}
function modal(title, body, form = "") {
  dialog.innerHTML = `<div class="modal-head"><h2 id="modal-title">${esc(title)}</h2><button data-action="modal.close" aria-label="إغلاق">×</button></div><div class="modal-body">${body}</div>`;
  if (!dialog.open) {
    if (dialog.showModal) dialog.showModal();
    else dialog.setAttribute("open", "");
  }
  setTimeout(
    () => dialog.querySelector("input:not([type=hidden]),select")?.focus(),
    100,
  );
}
function closeModal() {
  if (dialog.close) dialog.close();
  else dialog.removeAttribute("open");
  if (confirmResolve) {
    confirmResolve(false);
    confirmResolve = null;
  }
}
function confirm(title, message) {
  return new Promise((resolve) => {
    modal(
      title,
      `<p class="section-hint">${esc(message)}</p><div class="modal-actions"><button class="btn" data-action="confirm.yes">تأكيد</button><button class="btn light" data-action="modal.close">إلغاء</button></div>`,
    );
    confirmResolve = resolve;
  });
}
const btn = (label, action, style = "btn", glyph = "", extra = "") =>
  `<button class="${style}" data-action="${action}" ${extra}>${glyph ? icon(glyph, 19) : ""}${esc(label)}</button>`;
const field = (label, name, value = "", type = "text", extra = "") =>
  `<label class="field"><span>${esc(label)}</span><input name="${name}" type="${type}" value="${esc(value)}" ${extra}></label>`;
const select = (label, name, options, value = "", extra = "") =>
  `<label class="field"><span>${esc(label)}</span><select name="${name}" ${extra}>${options.map((o) => `<option value="${esc(o[0])}" ${String(o[0]) === String(value) ? "selected" : ""}>${esc(o[1])}</option>`).join("")}</select></label>`;
const empty = (text, action = "", label = "") =>
  `<div class="card empty">${icon("folder")}<strong>${esc(text)}</strong>${action ? btn(label, action, "btn small", "", "") : ""}</div>`;
const saveButton = (label = "حفظ") =>
  `<button class="btn full" type="submit" data-write>${icon("check", 18)}${esc(label)}</button>`;
function header(title = "", back = false) {
  if (route === "inventory") return "";
  if (route === "invoice")
    return `<header class="topbar invoice-header"><button class="icon-btn bare" data-action="back" aria-label="رجوع">${icon("arrow")}</button><div class="grow"><h2>فاتورة ${invoice.kind === "Sale" ? "بيع" : "شراء"} جديدة</h2><small class="muted">${icon("receipt", 12)} ${invoice.id ? "مسودة #" + invoice.id : "مسودة"}</small></div><button class="icon-btn bare" data-action="back" aria-label="الرئيسية">${icon("arrow")}</button></header>`;
  return `<header class="topbar">${back ? `<div class="row"><button class="icon-btn bare" data-action="back" aria-label="رجوع">${icon("arrow")}</button><h2>${esc(title)}</h2></div>` : `<div class="brand"><img src="brand.jpg" alt="مدار سوفت"><strong>مدار سوفت</strong></div>`}<button class="icon-btn bare notification" data-go="${back ? "home" : "assistant"}" aria-label="${back ? "الرئيسية" : "التنبيهات والمراجعة"}">${icon(back ? "home" : "bell")}${!back && route === "home" && (data.lowStock || data.expiring) ? '<i class="notification-dot"></i>' : ""}</button></header>`;
}
function navigation() {
  const active =
    route === "home"
      ? "home"
      : route === "invoice" && invoice?.kind === "Sale"
        ? "sale"
        : route === "inventory"
          ? "inventory"
          : ["accounts", "parties", "shifts"].includes(route)
            ? "accounts"
            : "more";
  return `<nav class="bottom-nav" aria-label="التنقل الرئيسي">${[
    ["home", "الرئيسية", "home"],
    ["sale", "المبيعات", "chart"],
    ["inventory", "المخزون", "cube"],
    ["accounts", "الحسابات", "receipt"],
    ["more", "المزيد", "more"],
  ]
    .map(
      ([id, label, glyph]) =>
        `<button class="nav-item ${active === id ? "active" : ""}" data-go="${id}" ${active === id ? 'aria-current="page"' : ""}>${icon(glyph)}<span>${label}</span></button>`,
    )
    .join("")}</nav>`;
}
function frame(content, title = "", extra = "") {
  app.innerHTML = `<main class="screen route-${route} ${extra}">${header(title, !!title)}${S.training ? `<div class="training-banner">${icon("book", 17)}<span>وضع التدريب — بيانات منفصلة عن متجرك</span><button data-action="training.exit">إنهاء</button></div>` : ""}${content}</main>${route === "invoice" ? "" : navigation()}${guideStep >= 0 ? guide() : ""}`;
}
async function initialize() {
  const status = await rpc("status");
  if (!status.initialized) return auth(true);
  if (!status.user) return auth(false);
  await loadState();
  await go("home", true);
}
async function loadState() {
  const previous = { user: S.user?.id, training: !!S.training };
  S = await rpc("state");
  if (
    previous.user &&
    (previous.user !== S.user?.id || previous.training !== !!S.training)
  )
    resetAiSession();
  settings = Object.fromEntries(S.settings.map((s) => [s.key, s.value]));
  if (S.backupWarning) toast(S.backupWarning);
}
function auth(first) {
  route = "auth";
  app.innerHTML = `<main class="auth"><img class="auth-logo" src="brand.jpg" alt="شعار مدار سوفت"><h1>مدار سوفت</h1><p class="subtitle">${first ? "لنبدأ بمتجرك، على هاتفك" : "أهلاً بعودتك إلى متجرك"}</p><section class="card"><h2>${first ? "إنشاء حساب المدير" : "تسجيل الدخول"}</h2><p class="section-hint">${first ? "حساب محلي لحماية عمليات المتجر. احفظ الاسم والرمز في مكان آمن." : "أدخل اسم المستخدم ورمزك السري."}</p><form data-form="${first ? "bootstrap" : "login"}">${first ? field("اسم المتجر", "storeName", "", "text", 'required maxlength="100"') + field("العملة المستخدمة", "currency", "", "text", 'required maxlength="20" placeholder="مثال: ر.س"') : ""}${field("اسم المستخدم", "name", "", "text", 'required maxlength="100" autocomplete="username"')}${field("الرمز السري", "pin", "", "password", 'required minlength="6" maxlength="12" inputmode="numeric" autocomplete="' + (first ? "new-password" : "current-password") + '"')}${first ? field("تأكيد الرمز السري", "confirm", "", "password", 'required minlength="6" maxlength="12" inputmode="numeric"') : ""}${saveButton(first ? "إنشاء المتجر" : "دخول")}<p class="auth-form-note">${first ? "رمز من 6 إلى 12 رقماً. لا توجد كلمة مرور افتراضية." : "لا يوجد اتصال بخادم أو حساب عبر الإنترنت."}</p></form></section><p class="offline"><i></i>يعمل محلياً دون إنترنت أو كمبيوتر</p></main>`;
}
async function go(next, force = false) {
  if (next === "sale" || next === "purchase") {
    if (
      !force &&
      route === "invoice" &&
      invoice?.dirty &&
      !(await confirm(
        "مغادرة الفاتورة",
        "احفظ المسودة أولاً إذا أردت العودة إليها. هل تريد ترك التغييرات غير المحفوظة؟",
      ))
    )
      return;
    await newInvoice(next === "sale" ? "Sale" : "Purchase");
    return;
  }
  if (
    !force &&
    route === "invoice" &&
    invoice?.dirty &&
    !(await confirm(
      "مغادرة الفاتورة",
      "التغييرات الحالية غير محفوظة. يمكنك إلغاء المغادرة وحفظ مسودة. هل تريد المغادرة؟",
    ))
  )
    return;
  route = next;
  switch (next) {
    case "home":
      data = await rpc("dashboard");
      home();
      break;
    case "inventory":
      data = await rpc("products", inventoryFilter);
      inventoryView();
      break;
    case "accounts":
      await loadState();
      data = await rpc("dashboard");
      accountsView();
      break;
    case "parties":
      data = await rpc("parties");
      partiesView();
      break;
    case "documents":
      data = await rpc("documents");
      documentsView();
      break;
    case "reports":
      context.reportFrom ||= today().slice(0, 8) + "01";
      context.reportTo ||= today();
      reportRows = await rpc("reports", {
        kind: reportType,
        from: context.reportFrom,
        to: context.reportTo,
      });
      reportsView();
      break;
    case "settings":
      await loadState();
      settingsView();
      break;
    case "users":
      data = await rpc("users");
      usersView();
      break;
    case "audit":
      data = await rpc("audit");
      auditView();
      break;
    case "shifts":
      data = await rpc("shifts");
      shiftsView();
      break;
    case "backup":
      await loadState();
      backupView();
      break;
    case "assistant":
      data = await rpc("assistant");
      aiState.info = await rpc("ai.status");
      assistantView();
      break;
    case "training":
      trainingView();
      break;
    default:
      moreView();
      break;
  }
  window.scrollTo(0, 0);
}
function metric(label, value, glyph) {
  return `<div class="card metric"><div class="row"><p>${esc(label)}</p><span class="medallion">${icon(glyph, 19)}</span></div><strong>${value}</strong></div>`;
}
function documentRows(rows) {
  return rows
    .map(
      (r) =>
        `<button class="document-row" data-action="document" data-id="${r.id}"><span class="document-icon">${icon(r.kind === "Purchase" ? "cart" : "receipt", 19)}</span><span class="grow"><strong>${esc(kinds[r.kind] || r.kind)} #${r.id}</strong><span class="small muted">${esc(r.partyName || "نقدي")} · ${date(r.createdAt)}</span></span><span><strong class="amount">${money(r.total)}</strong><span class="small"><span class="pill ${r.status === "Draft" ? "draft" : ""}">${r.status === "Draft" ? "مسودة" : "معتمدة"}</span></span></span></button>`,
    )
    .join("");
}
let artSerial = 0;
function productArt(name = "", category = "") {
  const key = `art-${++artSerial}`;
  const label = name + " " + category;
  if (/حليب|لبن|ألبان/.test(label))
    return `<svg viewBox="0 0 60 84" aria-hidden="true"><defs><linearGradient id="${key}" x2="1"><stop stop-color="#d8e2e8"/><stop offset=".4" stop-color="#fff"/><stop offset="1" stop-color="#c9d5dd"/></linearGradient></defs><ellipse cx="30" cy="80" rx="16" ry="3" fill="#dbe6ee"/><path d="M22 12h16v10c0 6 7 11 7 20v32c0 7-30 7-30 0V42c0-9 7-14 7-20z" fill="url(#${key})" stroke="#cdd8e1"/><rect x="22" y="6" width="16" height="9" rx="2" fill="#009ce5"/><path d="M23 7h14" stroke="#56dcff" stroke-width="2"/><path d="M19 40c5-3 17-3 22 0v17H19z" fill="#edf7fc"/><path d="M21 43h18M21 47h15" stroke="#bedbec"/><path d="M20 28c-3 6-3 9-3 13" stroke="#fff" stroke-width="2" fill="none"/></svg>`;
  if (/أرز|ارز|حبوب/.test(label))
    return `<svg viewBox="0 0 70 84" aria-hidden="true"><defs><linearGradient id="${key}" x2="1"><stop stop-color="#aa8051"/><stop offset=".45" stop-color="#ead0a2"/><stop offset="1" stop-color="#b38a5a"/></linearGradient></defs><ellipse cx="35" cy="79" rx="24" ry="3" fill="#e1ddd5"/><path d="M15 29c4 4 36 4 40 0l5 43c1 9-51 9-50 0z" fill="url(#${key})" stroke="#b18b62"/><path d="M15 29 11 19c8-7 40-7 48 0l-4 10c-13 6-27 6-40 0z" fill="#c5a575" stroke="#a58559"/><ellipse cx="35" cy="21" rx="23" ry="8" fill="#f4e6c7"/><path d="M15 21c7-16 33-17 40 0" fill="#fff5db"/><path d="m25 18 4-3m5 2 4-3m-17 6 4-2m17 1 4-3m-10 6 4-2" stroke="#d6c399" stroke-width="1.5"/><path d="M16 34c4 10 0 29 0 38m39-38c-4 10 0 29 0 38" stroke="#a88861" fill="none" opacity=".5"/></svg>`;
  if (/سكر/.test(label))
    return `<svg viewBox="0 0 66 84" aria-hidden="true"><defs><linearGradient id="${key}" x2="1"><stop stop-color="#d8dee5"/><stop offset=".5" stop-color="#fff"/><stop offset="1" stop-color="#d1d9e1"/></linearGradient></defs><ellipse cx="33" cy="79" rx="22" ry="3" fill="#dde5ed"/><path d="M12 12h42l-5 13 8 49c0 7-48 7-48 0l8-49z" fill="url(#${key})" stroke="#c6d0da"/><path d="M12 13c11 3 31 3 42 0m-37 12c8 3 25 3 32 0M12 74c9-4 33-4 42 0" fill="none" stroke="#c8d2dc"/><path d="M20 30c1 15-3 28-4 36m30-35c-1 15 3 28 4 36" fill="none" stroke="#edf2f5" stroke-width="3"/></svg>`;
  return icon("cube", 32);
}
function salesTrend(rows) {
  const totals = [];
  for (let i = 6; i >= 0; i--) {
    const day = new Date();
    day.setDate(day.getDate() - i);
    const key = new Date(day - day.getTimezoneOffset() * 60000)
      .toISOString()
      .slice(0, 10);
    totals.push(rows.find((r) => r.day === key)?.total || 0);
  }
  const max = Math.max(...totals, 1);
  const points = totals
    .map((v, i) => `${8 + i * 20},${48 - (v / max) * 35}`)
    .join(" ");
  return `<svg class="sales-sparkline" viewBox="0 0 136 60" role="img" aria-label="اتجاه المبيعات الفعلية خلال آخر سبعة أيام"><defs><linearGradient id="sales-fill" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#00dfff" stop-opacity=".28"/><stop offset="1" stop-color="#00dfff" stop-opacity="0"/></linearGradient></defs><polygon points="8,58 ${points} 128,58" fill="url(#sales-fill)"/><polyline points="${points}" fill="none" stroke="#16dcff" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>${totals.map((v, i) => `<circle cx="${8 + i * 20}" cy="${48 - (v / max) * 35}" r="2.3" fill="#adf9ff"/>`).join("")}</svg>`;
}
function home() {
  const d = data;
  const quick = [
    ["sale", "بيع جديد", "cart", "primary"],
    ["purchase", "شراء جديد", "cart", "warm"],
    ["receipt", "سند قبض", "receipt", "soft"],
  ].filter((x) => writer() && (owner() || x[0] !== "purchase"));
  frame(
    `<section class="intro"><h1>مرحباً، ${esc(S.user.name)}</h1><p>نظرة عامة على متجرك</p></section><section class="card hero sales-hero"><div class="sales-summary"><span class="medallion">${icon("chart", 20)}</span><div><p>مبيعات اليوم</p><strong class="amount">${money(d.sales)}</strong></div></div><button class="sales-trend" data-go="reports" aria-label="عرض تقرير المبيعات">${salesTrend(d.weekly)}<span>${icon("arrow", 16)}</span></button></section><section class="grid home-metrics">${metric("رصيد الصندوق", money(d.cash), "wallet")}${metric("فواتير اليوم", Number(d.invoices).toLocaleString("ar"), "receipt")}</section>${quick.length ? `<div class="section-title"><h3>إجراءات سريعة</h3></div><div class="quick-actions">${quick.map(([id, label, glyph, style]) => `<button class="quick ${style}" ${id === "receipt" ? 'data-action="cash" data-kind="Receipt"' : `data-go="${id}"`}><span>${icon(glyph)}</span>${label}</button>`).join("")}</div>` : ""}<div class="section-title"><h3>آخر العمليات</h3><button class="text-btn" data-go="documents">عرض الكل</button></div>${d.recent.length ? `<section class="recent-list">${documentRows(d.recent.slice(0, 3))}</section>` : empty("لم تُسجّل عمليات بعد", writer() ? "shift.open" : "", writer() ? "فتح وردية الصندوق" : "")}${d.lowStock || d.expiring ? `<button class="notice row stock-notice" data-go="inventory">${icon("bell", 18)}<span>${Number(d.lowStock).toLocaleString("ar")} أصناف عند حد المخزون، و${Number(d.expiring).toLocaleString("ar")} دفعات تستحق مراجعة الصلاحية</span></button>` : ""}`,
  );
}
function weeklyChart(rows) {
  const days = [];
  for (let i = 6; i >= 0; i--) {
    const d = new Date();
    d.setDate(d.getDate() - i);
    const key = new Date(d - new Date().getTimezoneOffset() * 60000)
      .toISOString()
      .slice(0, 10);
    days.push({
      label: d.toLocaleDateString("ar", { weekday: "short" }),
      total: rows.find((r) => r.day === key)?.total || 0,
    });
  }
  const max = Math.max(...days.map((d) => d.total), 1);
  return `<div class="bar-chart">${days.map((d) => `<div class="bar"><span class="bar-value">${d.total ? money(d.total, false) : ""}</span><b style="height:${Math.max(3, (d.total / max) * 85)}px"></b><span>${esc(d.label)}</span></div>`).join("")}</div>`;
}
function inventoryView() {
  const d = data;
  frame(
    `<div class="section-title inventory-heading"><div class="row">${icon("cube", 25)}<h1>المخزون</h1></div>${owner() ? btn("إضافة منتج", "product.new", "btn gold-btn small", "plus") : ""}</div><form data-form="inventory.search" class="search"><input name="search" placeholder="ابحث عن منتج" value="${esc(inventoryFilter.search)}" aria-label="بحث عن منتج"><button class="icon-btn bare" aria-label="بحث">${icon("search", 20)}</button></form><div class="inventory-filters"><button class="chip ${!inventoryFilter.low ? "active" : ""}" data-action="inventory.all">الكل</button><select id="category-filter" aria-label="التصنيف"><option value="">التصنيفات</option>${d.categories.map((c) => `<option ${c.name === inventoryFilter.category ? "selected" : ""}>${esc(c.name)}</option>`).join("")}</select><select id="warehouse-filter" aria-label="المستودع"><option value="0">المستودع</option>${d.warehouses.map((w) => `<option value="${w.id}" ${w.id === inventoryFilter.warehouse ? "selected" : ""}>${esc(w.name)}</option>`).join("")}</select></div><details class="inventory-tools"><summary>خيارات المخزون ${icon("more", 16)}</summary><div class="filters"><button class="chip ${inventoryFilter.low ? "active" : ""}" data-action="inventory.low">عند حد المخزون</button><button class="chip" data-action="lots">الدفعات والصلاحية</button>${owner() ? '<button class="chip" data-action="warehouse.new">مستودع جديد</button>' : ""}</div></details><div class="stack product-list">${d.items.length ? d.items.map((p) => `<button class="card product" data-action="product" data-id="${p.id}"><span class="product-art">${productArt(p.name, p.category)}</span><span class="grow product-main"><h3>${esc(p.name)}</h3><p>${esc(p.category || "دون تصنيف")}</p></span><span class="product-count"><strong>${quantity(p.quantity)} ${esc(p.baseUnit)}</strong>${p.quantity <= p.minimum ? '<span class="pill danger">مخزون منخفض</span>' : ""}${!p.enabled ? '<span class="pill draft">معطل</span>' : ""}</span><span class="product-more">⋮</span></button>`).join("") : empty("لا توجد منتجات مطابقة", owner() ? "product.new" : "", owner() ? "إضافة أول منتج" : "")}</div>`,
    "",
    "inventory-page",
  );
}
async function productDetail(id) {
  const p = data.items.find((p) => p.id === id);
  if (!p) return;
  const units = data.units.filter((u) => u.productId === id);
  const history = data.prices.filter((h) => h.productId === id).slice(0, 8);
  context.product = p;
  const unitRows = units
    .map(
      (u) =>
        `<div class="summary-line"><span>${esc(u.name)}<small class="muted"> × ${quantity(u.factor)} ${esc(p.baseUnit)}</small></span><span><strong>${money(u.price)}</strong>${owner() ? btn("تعديل", "unit.edit", "text-btn", "", `data-id="${u.id}"`) : ""}</span></div>`,
    )
    .join("");
  modal(
    p.name,
    `<div class="card"><div class="summary-line"><span>المخزون</span><strong>${quantity(p.quantity)} ${esc(p.baseUnit)}</strong></div><div class="summary-line"><span>القيمة الدفترية</span><strong>${money(p.value)}</strong></div><div class="summary-line"><span>الباركود</span><span>${esc(p.barcode || "—")}</span></div></div><div class="section-title"><h3>الوحدات وأسعار البيع</h3>${owner() ? btn("إضافة", "unit.new", "text-btn", "plus") : ""}</div>${unitRows}${history.length ? `<div class="section-title"><h3>آخر تغييرات الأسعار</h3></div>${history.map((h) => `<p class="section-hint">${esc(h.unitName)}: ${money(h.oldPrice)} ← ${money(h.newPrice)} · ${date(h.createdAt)}</p>`).join("")}` : ""}${owner() ? `<div class="modal-actions">${btn("تعديل المنتج", "product.edit", "btn", "settings")}${btn("جرد / تسوية", "stock.adjust", "btn outline", "cube")}</div>` : ""}`,
  );
}
async function newInvoice(kind) {
  if (!writer()) throw Error("الحساب للعرض فقط");
  if (kind === "Purchase" && !owner())
    throw Error("الشراء يحتاج صلاحية المدير");
  invoice = {
    kind,
    requestId: uuid(),
    warehouseId: 1,
    partyId: 0,
    full: true,
    paid: 0,
    note: "",
    dueDate: "",
    lines: [],
    dirty: false,
  };
  route = "invoice";
  await invoiceData();
  invoiceView();
}
async function invoiceData() {
  data = {
    catalog: await rpc("products"),
    parties: await rpc("parties", {
      kind: invoice.kind === "Sale" ? "Customer" : "Supplier",
    }),
  };
}
function lineTotal(line) {
  return Math.round((line.quantity * line.price + Number.EPSILON) * 100);
}
function invoiceView() {
  const d = invoice;
  const total = d.lines.reduce((v, l) => v + lineTotal(l), 0);
  frame(
    `<div class="invoice-party">${select(d.kind === "Sale" ? "العميل" : "المورد", "partyId", [[0, d.kind === "Sale" ? "اختر العميل — بيع نقدي" : "اختر المورد — شراء نقدي"], ...data.parties.map((p) => [p.id, p.name])], d.partyId, 'id="invoice-party"')}</div><form data-form="invoice.barcode" class="search"><input name="barcode" placeholder="ابحث عن منتج أو أدخل الباركود" aria-label="باركود"><button type="button" class="icon-btn bare" data-action="barcode" aria-label="تصوير باركود">${icon("camera", 20)}</button></form>${btn("إضافة منتج إلى الفاتورة", "invoice.add", "btn light full invoice-add", "plus")}<div class="stack invoice-lines">${
      d.lines.length
        ? d.lines
            .map(
              (l, i) =>
                `<section class="card invoice-item"><span class="product-art">${productArt(l.name)}</span><div class="invoice-line-info"><h3>${esc(l.name)}</h3><select class="unit-select" data-line-unit="${i}" aria-label="وحدة ${esc(l.name)}">${data.catalog.units
                  .filter((u) => u.productId === l.productId)
                  .map(
                    (u) =>
                      `<option value="${u.id}" ${u.id === l.unitId ? "selected" : ""}>${esc(u.name)}</option>`,
                  )
                  .join(
                    "",
                  )}</select><label class="sub price-label">سعر الوحدة <input class="price-field" data-line-price="${i}" value="${l.price}" inputmode="decimal" aria-label="سعر ${esc(l.name)}"></label></div><div class="line-amount"><div class="quantity-control"><button data-action="invoice.minus" data-index="${i}" aria-label="تقليل الكمية">−</button><input data-line-quantity="${i}" value="${l.quantity}" inputmode="decimal" aria-label="كمية ${esc(l.name)}"><button data-action="invoice.plus" data-index="${i}" aria-label="زيادة الكمية">+</button></div><strong class="total">${money(lineTotal(l), false)}<small>${currency()}</small></strong><button class="line-remove" data-action="invoice.remove" data-index="${i}" aria-label="حذف الصنف">${icon("trash", 14)}</button></div>${d.kind === "Purchase" ? `<div class="grid purchase-batch">${field("رقم الدفعة", "batch", l.batch || "", "text", `data-line-batch="${i}"`)}${field("الصلاحية (اختياري)", "expiry", l.expiry || "", "date", `data-line-expiry="${i}"`)}</div>` : ""}</section>`,
            )
            .join("")
        : '<div class="empty">أضف المنتجات بالبحث أو الباركود</div>'
    }</div><details class="invoice-advanced"><summary>المستودع والملاحظات</summary>${select(
      "المستودع",
      "warehouseId",
      data.catalog.warehouses.map((w) => [w.id, w.name]),
      d.warehouseId,
      'id="invoice-warehouse"',
    )}<label class="field"><span>ملاحظات</span><textarea id="invoice-note" maxlength="500" placeholder="أضف ملاحظة للفاتورة">${esc(d.note)}</textarea></label></details><section class="checkout"><div class="sum"><span>الإجمالي</span><strong>${money(total)}</strong></div><p class="checkout-label">طريقة الدفع</p><div class="segmented"><button class="${d.full ? "active" : ""}" data-action="invoice.full">${icon("wallet", 18)} نقدي</button><button class="${!d.full ? "active" : ""}" data-action="invoice.credit">${icon("clock", 18)} آجل / جزئي</button></div>${!d.full ? `<div class="grid credit-fields">${field("المدفوع الآن", "paid", d.paid, "number", 'id="invoice-paid" min="0" step="0.01"')}${field("تاريخ الاستحقاق", "dueDate", d.dueDate, "date", 'id="invoice-due"')}</div>` : ""}<p class="checkout-label">الحساب</p><div class="cash-account">${icon("wallet", 17)}<span>الصندوق الرئيسي</span>${icon("check", 14)}</div><div class="buttons">${btn("اعتماد الفاتورة", "invoice.post", "btn", "check", "data-write")}${btn("حفظ مسودة", "invoice.save", "btn outline", "receipt", "data-write")}</div></section>`,
    "",
    "invoice-page",
  );
  requestAnimationFrame(() => {
    const checkout = $(".checkout");
    const page = $(".invoice-page");
    if (checkout && page && !document.body.classList.contains("keyboard"))
      page.style.paddingBottom = `${checkout.offsetHeight + 24}px`;
  });
}
function accountsView() {
  const d = data;
  const shift = S.shift;
  frame(
    `<section class="intro"><h1>حسابات المتجر</h1><p>الصندوق والديون في مكان واحد</p></section><section class="card hero"><div class="row between"><p>رصيد الصندوق</p><span class="medallion">${icon("wallet")}</span></div><div class="amount">${money(d.cash)}</div></section><div class="grid" style="margin-top:12px">${metric("لنا عند العملاء", money(d.receivable), "users")}${metric("للموردين", money(d.payable), "cart")}</div><div class="section-title"><h3>وردية الصندوق</h3><button class="text-btn" data-go="shifts">السجل</button></div><section class="card">${shift ? `<div class="row between"><strong>#${shift.id} · ${esc(shift.employee)}</strong><span class="pill ${shift.status === "Review" ? "danger" : ""}">${shift.status === "Review" ? "بانتظار الاعتماد" : "مفتوحة"}</span></div><p class="section-hint">افتتاح: ${money(shift.openingCash)} · ${date(shift.openedAt)}</p>${writer() ? (shift.status === "Review" ? (owner() ? btn("مراجعة واعتماد الفرق", "shift.approve", "btn full", "", 'data-write data-id="' + shift.id + '"') : '<p class="notice">تحتاج مراجعة المدير قبل متابعة العمليات.</p>') : btn("إغلاق وعدّ الصندوق", "shift.close", "btn outline full", "wallet")) : ""}` : `<p class="section-hint">افتح وردية قبل تسجيل عمليات مالية جديدة.</p>${writer() ? btn("فتح وردية الصندوق", "shift.open", "btn full", "plus", "data-write") : ""}`}</section><div class="section-title"><h3>حركة نقدية جديدة</h3></div><div class="grid">${[
      ["Receipt", "تحصيل من عميل", "receipt"],
      ["Payment", "سداد لمورد", "cart"],
      ["Capital", "تمويل الصندوق", "wallet"],
      ["Expense", "مصروف", "receipt"],
    ]
      .filter((x) => writer() && (owner() || x[0] === "Receipt"))
      .map(([kind, label, glyph]) =>
        btn(
          label,
          "cash",
          "btn " + (kind === "Receipt" ? "" : "light"),
          glyph,
          `data-kind="${kind}"`,
        ),
      )
      .join(
        "",
      )}</div><div class="section-title"><h3>الأطراف والاستحقاقات</h3></div><div class="card list-card"><button class="menu-row" data-go="parties"><span class="menu-icon">${icon("users")}</span><span><strong>العملاء والموردون</strong><small>الأرصدة ووسائل الاتصال</small></span>${icon("arrow")}</button><button class="menu-row" data-action="reports.debts"><span class="menu-icon">${icon("calendar")}</span><span><strong>الفواتير المستحقة</strong><small>الآجل والتأخيرات</small></span>${icon("arrow")}</button></div>`,
  );
}
function partiesView() {
  frame(
    `<div class="section-title"><h1>العملاء والموردون</h1>${writer() ? btn("إضافة", "party.new", "btn gold-btn small", "plus") : ""}</div><div class="stack">${data.length ? data.map((p) => `<section class="card"><div class="row"><span class="medallion">${icon(p.kind === "Customer" ? "users" : "cart")}</span><div class="grow"><h3>${esc(p.name)}</h3><p class="small muted">${p.kind === "Customer" ? "عميل" : "مورد"} ${p.phone ? "· " + esc(p.phone) : ""}</p></div><strong>${money(p.balance)}</strong></div><div class="info-line"><span class="grow">الاستحقاق: ${p.nextDue ? date(p.nextDue) : "لا توجد فواتير مستحقة"}</span>${writer() ? btn(p.kind === "Customer" ? "تحصيل" : "سداد", "cash", "text-btn", "", `data-kind="${p.kind === "Customer" ? "Receipt" : "Payment"}" data-party="${p.id}"`) : ""}</div>${owner() ? btn("تعديل البيانات", "party.edit", "text-btn", "", `data-id="${p.id}"`) : ""}</section>`).join("") : empty("أضف العملاء والموردين لتتابع أرصدتهم", writer() ? "party.new" : "", "إضافة طرف")}</div>`,
    "الأطراف",
  );
}
function documentsView() {
  frame(
    `<section class="intro"><h1>سجل العمليات</h1><p>الفواتير والحركات والمسودات المحفوظة</p></section><div class="filters">${[
      ["", "الكل"],
      ["Draft", "المسودات"],
      ["Posted", "المعتمدة"],
    ]
      .map(
        ([id, name]) =>
          `<button class="chip" data-action="documents.filter" data-status="${id}">${name}</button>`,
      )
      .join(
        "",
      )}</div>${data.length ? `<section class="card" style="padding:0 15px">${documentRows(data)}</section>` : empty("لا توجد عمليات في هذا العرض")}`,
    "العمليات",
  );
}
const reportNames = {
  income: "قائمة الدخل",
  profit: "ربحية الأصناف",
  trial: "ميزان المراجعة",
  journal: "دفتر اليومية",
  suppliers: "مقارنة أسعار الموردين",
  debts: "الديون والاستحقاقات",
  stock: "قيمة المخزون",
  expiry: "الدفعات والصلاحية",
};
const columnNames = {
  account: "الحساب",
  debit: "مدين",
  credit: "دائن",
  productId: "معرّف الصنف",
  name: "الصنف / الاسم",
  quantity: "الكمية",
  sales: "المبيعات",
  cost: "التكلفة",
  profit: "الربح",
  supplier: "المورد",
  unitName: "وحدة الشراء",
  baseQuantity: "الكمية الأساسية",
  pricePerBase: "سعر الوحدة الأساسية",
  total: "الإجمالي",
  createdAt: "التاريخ",
  invoice: "رقم الفاتورة",
  dueDate: "الاستحقاق",
  remaining: "المتبقي",
  kind: "النوع",
  baseUnit: "الوحدة الأساسية",
  warehouse: "المستودع",
  value: "القيمة",
  minimum: "حد التنبيه",
  batch: "الدفعة",
  expiry: "الصلاحية",
  document: "رقم العملية",
  party: "الطرف",
};
const moneyColumns = new Set([
  "debit",
  "credit",
  "sales",
  "cost",
  "profit",
  "total",
  "pricePerBase",
  "value",
]);
function cellValue(key, value) {
  if (value === null || value === undefined) return "—";
  if (moneyColumns.has(key) || (key === "remaining" && reportType === "debts"))
    return money(value, false);
  if (
    ["quantity", "baseQuantity", "minimum"].includes(key) ||
    (key === "remaining" && reportType === "expiry")
  )
    return quantity(value);
  if (key === "account") return esc(accounts[value] || value);
  if (key === "kind")
    return esc(
      kinds[value] || { Customer: "عميل", Supplier: "مورد" }[value] || value,
    );
  if (["createdAt", "dueDate", "expiry"].includes(key)) return date(value);
  return esc(value);
}
function reportsView() {
  if (reportType === "suppliers")
    reportRows = reportRows.map((r) => ({
      ...r,
      pricePerBase: r.baseQuantity ? (r.total * 1000) / r.baseQuantity : 0,
    }));
  const columns = reportRows.length
    ? Object.keys(reportRows[0]).filter((k) => k !== "productId")
    : [];
  frame(
    `<section class="intro"><h1>تقارير متجرك</h1><p>أرقام من عملياتك الفعلية · ${currency()}</p></section><form data-form="report"><label class="field"><span>التقرير</span><select name="kind">${Object.entries(
      reportNames,
    )
      .map(
        ([id, name]) =>
          `<option value="${id}" ${id === reportType ? "selected" : ""}>${name}</option>`,
      )
      .join(
        "",
      )}</select></label><div class="grid">${field("من", "from", context.reportFrom || today().slice(0, 8) + "01", "date", "required")}${field("إلى", "to", context.reportTo || today(), "date", "required")}</div>${saveButton("عرض التقرير")}</form><p class="section-hint">${reportType === "trial" ? "ميزان المراجعة تراكمي حتى تاريخ النهاية." : reportType === "suppliers" ? "مقارنة الدفعات المسجّلة حسب المورد والتاريخ، مع حساب السعر للوحدة الأساسية." : reportType === "debts" ? "المديونية المتبقية بعد التحصيل والسداد موزّعة على الاستحقاقات." : "تُعرض البيانات المتاحة ضمن حدود التقرير."}</p>${reportRows.length ? `<div class="row wrap">${btn("Excel", "export.xlsx", "btn outline small", "download")}${btn("CSV", "export.csv", "btn outline small", "download")}</div><div class="card table-scroll" style="padding:0"><table><thead><tr>${columns.map((k) => `<th>${esc(columnNames[k] || k)}</th>`).join("")}</tr></thead><tbody>${reportRows.map((r) => `<tr>${columns.map((k) => `<td>${cellValue(k, r[k])}</td>`).join("")}</tr>`).join("")}</tbody></table></div><p class="small muted" style="margin-top:10px">${Number(reportRows.length).toLocaleString("ar")} صفوف معروضة</p>` : empty("لا توجد بيانات لهذا التقرير")}`,
    "التقارير",
  );
}
function moreView() {
  const rows = [
    ["documents", "سجل العمليات", "الفواتير والمسودات والمرفقات", "receipt"],
    ["reports", "التقارير", "الربحية والاستحقاقات والمخزون", "chart"],
    ["parties", "العملاء والموردون", "الأرصدة وبيانات الأطراف", "users"],
    ["assistant", "المساعد المحلي", "إرشاد محاسبي وتنبيهات المراجعة", "robot"],
    ["training", "التدريب", "تعلّم على بيانات منفصلة", "book"],
    [
      "backup",
      "النسخ والبيانات",
      "النسخ الاحتياطي والاستيراد والتصدير",
      "folder",
    ],
    ...(owner()
      ? [
          [
            "users",
            "الموظفون والصلاحيات",
            "الحسابات المحلية وأذونات العمل",
            "shield",
          ],
          ["audit", "سجل التوثيق", "صاحب العملية ووقتها", "clock"],
          [
            "settings",
            "إعدادات المتجر",
            "العملة والطباعة وبيانات المتجر",
            "settings",
          ],
        ]
      : []),
  ];
  frame(
    `<section class="intro"><h1>كل ما تحتاجه</h1><p>${esc(S.user.name)} · ${esc({ Owner: "مدير", Cashier: "كاشير", Viewer: "عرض فقط" }[S.user.role])}</p></section><section class="card list-card">${rows.map(([id, title, sub, glyph]) => `<button class="menu-row" data-go="${id}"><span class="menu-icon">${icon(glyph)}</span><span><strong>${title}</strong><small>${sub}</small></span>${icon("arrow")}</button>`).join("")}<button class="menu-row danger-text" data-action="logout"><span class="menu-icon">${icon("logout")}</span><strong>تسجيل الخروج</strong>${icon("arrow")}</button></section><p class="section-hint" style="text-align:center;margin-top:24px">مدار سوفت 2.1 · بياناتك على هاتفك</p>`,
  );
}
function settingsView() {
  frame(
    `<section class="intro"><h1>إعدادات المتجر</h1><p>تظهر هذه البيانات على الفواتير</p></section><section class="card"><form data-form="settings">${field("اسم المتجر", "storeName", settings.storeName, "text", 'required maxlength="100"')}${field("العملة", "currency", settings.currency, "text", 'required maxlength="20"')}${field("الهاتف", "phone", settings.phone || "", "tel", 'maxlength="50"')}${field("العنوان", "address", settings.address || "", "text", 'maxlength="250"')}${select(
      "مقاس الفاتورة",
      "paper",
      [
        ["80", "رول 80 مم"],
        ["58", "رول 58 مم"],
        ["A4", "ورق A4"],
      ],
      settings.paper || "80",
    )}${saveButton("حفظ الإعدادات")}</form></section><p class="section-hint">الطباعة تستخدم خيارات أندرويد. يعتمد توفر الطابعة والمقاس على خدمة الطباعة والطابعة المثبتتين على هاتفك.</p>`,
    "الإعدادات",
  );
}
function backupView() {
  frame(
    `<section class="intro"><h1>النسخ والبيانات</h1><p>نسخة خارج الهاتف تحميك عند فقدانه</p></section><section class="card"><h3>آخر نسخة تلقائية</h3><p class="section-hint">${S.lastBackup ? esc(new Date(S.lastBackup).toLocaleString("ar")) : "لم تُنشأ نسخة تلقائية بعد"}</p><p class="small muted">تُحدّث بعد حفظ العمليات المالية والمرفقات. النسخة داخل الهاتف لا تحمي من فقدانه؛ شارك نسخة إلى مكان آخر.</p>${S.backupWarning ? `<p class="notice error">${esc(S.backupWarning)}</p>` : ""}</section>${owner() ? `<div class="stack" style="margin-top:15px">${btn("حفظ / مشاركة نسخة احتياطية", "backup.create", "btn full", "download", "data-write")}${btn("استعادة نسخة احتياطية", "backup.restore", "btn outline full", "folder")}</div><section class="card" style="margin-top:24px"><h3>استيراد المنتجات</h3><p class="section-hint">CSV أو XLSX، مع معاينة وفحص الباركود المكرر قبل الاعتماد. يستورد تعريف المنتجات والأسعار؛ تُدخل الكميات بفواتير شراء.</p><div class="stack">${btn("اختيار ملف ومعاينته", "import.preview", "btn light full", "folder")}${btn("تنزيل نموذج Excel", "import.template", "btn outline full", "download")}</div></section>` : '<p class="notice" style="margin-top:18px">إدارة النسخ والاستيراد متاحة للمدير.</p>'}<p class="section-hint">الاستعادة تتحقق من سلامة الملفات والقاعدة قبل استبدال البيانات، وتحفظ نسخة من الوضع السابق.</p>`,
    "النسخ الاحتياطي",
  );
}
function usersView() {
  frame(
    `<div class="section-title"><h1>الموظفون</h1>${btn("حساب جديد", "user.new", "btn gold-btn small", "plus")}</div><div class="stack">${data.map((u) => `<button class="card row" style="text-align:right" data-action="user.edit" data-id="${u.id}"><span class="medallion">${icon("users")}</span><span class="grow"><h3>${esc(u.name)}</h3><small class="muted">${esc({ Owner: "مدير — جميع العمليات", Cashier: "كاشير — البيع والتحصيل", Viewer: "عرض فقط" }[u.role])}</small></span><span class="pill ${u.enabled ? "" : "danger"}">${u.enabled ? "مفعّل" : "معطل"}</span></button>`).join("")}</div><p class="section-hint">التحقق من الصلاحيات يتم عند تنفيذ العملية، وليس بإخفاء الأزرار فقط. لكل موظف رمز دخول مستقل.</p>`,
    "الصلاحيات",
  );
}
function auditView() {
  frame(
    `<section class="intro"><h1>سجل التوثيق</h1><p>آخر 500 عملية موثقة</p></section><div class="stack">${data.length ? data.map((a) => `<div class="card"><strong>${esc(a.action)}</strong><p class="section-hint">${esc(a.userName)} · ${esc(new Date(a.createdAt).toLocaleString("ar"))}${a.target ? " · #" + esc(a.target) : ""}</p></div>`).join("") : empty("لا توجد سجلات")}</div>`,
    "التوثيق",
  );
}
function shiftsView() {
  frame(
    `<section class="intro"><h1>ورديات الصندوق</h1><p>الافتتاح والإغلاق والفروق المسجلة</p></section><div class="stack">${data.length ? data.map((s) => `<section class="card"><div class="row between"><strong>وردية #${s.id}</strong><span class="pill ${s.status === "Review" ? "danger" : ""}">${{ Open: "مفتوحة", Review: "تحتاج اعتماداً", Closed: "مغلقة" }[s.status]}</span></div><p class="section-hint">${date(s.openedAt)}</p><div class="summary-line"><span>رصيد الافتتاح</span><strong>${money(s.openingCash)}</strong></div>${s.expectedCash !== null ? `<div class="summary-line"><span>المتوقع</span><strong>${money(s.expectedCash)}</strong></div><div class="summary-line"><span>النقد المعدود</span><strong>${money(s.countedCash)}</strong></div><p class="section-hint">${esc(s.reason || "")}</p>` : ""}${s.status === "Review" && owner() ? btn("مراجعة الفرق واعتماده", "shift.approve", "btn outline full", "", `data-id="${s.id}"`) : ""}</section>`).join("") : empty("لا توجد ورديات مسجلة")}</div>`,
    "الورديات",
  );
}

function resetAiSession() {
  aiState.epoch++;
  aiState.running = false;
  aiState.answer = "";
  aiState.question = "";
  aiState.error = "";
  aiState.facts = null;
  aiState.stopped = false;
}
function assistantView() {
  const info = aiState.info,
    ready = info?.status?.ready;
  const mb = Math.ceil((info?.recommended?.bytes || 484220320) / 1000000);
  const setup = `<section class="card ai-setup"><div class="row"><span class="medallion">${icon("robot")}</span><div><h3>${ready ? esc(info.status.model.name) : "جهّز المساعد لمرة واحدة"}</h3><p class="small muted">${info?.device ? esc(info.device) + " · " : ""}${info?.memoryMb ? Number(info.memoryMb / 1024).toLocaleString("ar", { maximumFractionDigits: 1 }) + " غيغابايت ذاكرة" : "وضع خفيف للهاتف"}</p></div></div>${!ready ? `<p class="section-hint">نزّل ملف الذكاء (نحو ${mb} ميغابايت) من المتصفح، ثم اختر «استيراد النموذج». بعد ذلك تعمل المحادثة دون إنترنت أو كمبيوتر أو اشتراك.</p><div class="stack">${btn("تنزيل ملف الذكاء", "ai.download", "btn full", "download")}${owner() ? btn("استيراد النموذج GGUF", "ai.import", "btn outline full", "folder") : "<p class='section-hint'>يجهّز مدير التطبيق النموذج أولاً، ثم يستطيع الموظفون استخدامه.</p>"}</div>` : `<p class="section-hint">النموذج جاهز على هاتفك. لا تُرسل الأسئلة أو أرقام المتجر إلى خادم.</p><details><summary>إدارة ملف الذكاء</summary><div class="stack" style="margin-top:12px">${btn("تنزيل الملف مجدداً", "ai.download", "btn light full", "download")}${owner() ? btn("استبدال ملف النموذج", "ai.import", "btn outline full", "folder") + btn("حذف النموذج من الهاتف", "ai.remove", "btn danger full", "trash") : ""}</div><p class="section-hint">ملف الذكاء منفصل عن بيانات المتجر والنسخة الاحتياطية.</p></details>`}</section>`;
  frame(
    `<section class="intro"><h1>المساعد الذكي المحلي</h1><p>اسأل بالعربية عن بياناتك أو المحاسبة الأساسية</p></section>${setup}${
      ready
        ? `<form class="ai-form" data-form="assistant.ai"><label class="field"><span>الفترة للأرقام المرجعية</span><select name="days" ${aiState.running ? "disabled" : ""}>${[
            [1, "اليوم"],
            [7, "آخر 7 أيام"],
            [30, "آخر 30 يوماً"],
          ]
            .map(
              ([id, label]) =>
                `<option value="${id}" ${id === aiState.days ? "selected" : ""}>${label}</option>`,
            )
            .join(
              "",
            )}</select></label><label class="field"><textarea name="question" placeholder="مثال: كم رصيد الصندوق؟ وما الفرق بين الربح والمبيعات؟" maxlength="400" required ${aiState.running ? "disabled" : ""}>${esc(aiState.question)}</textarea></label><div class="row"><button class="btn grow" type="submit" ${aiState.running ? "disabled" : ""}>${icon("robot", 18)}${aiState.running ? "جارٍ إعداد الرد…" : "اسأل المساعد"}</button>${aiState.running ? btn("إيقاف", "ai.cancel", "btn outline", "close") : ""}</div></form>`
        : ""
    }${aiState.error ? `<p class="notice error" role="alert">${esc(aiState.error)}</p>` : ""}${aiState.running || aiState.answer || aiState.stopped ? `<section class="card ai-response"><div class="row between"><h3>الرد</h3><span class="pill">${aiState.running ? "على هاتفك…" : aiState.stopped ? "توقف الرد" : "ذكاء محلي"}</span></div><p id="ai-answer" class="assistant-answer" aria-live="polite">${esc(aiState.answer || "يُحمّل النموذج ويقرأ ملخص الأرقام…")}</p>${aiState.model ? `<small class="muted">${esc(aiState.model)}</small>` : ""}</section>` : ""}${
      aiState.facts
        ? `<details class="card ai-facts"><summary>الأرقام المرجعية عند بدء السؤال</summary><p class="section-hint">${esc(aiState.facts.from)} — ${esc(aiState.facts.to)}</p>${[
            ["المبيعات", aiState.facts.sales],
            ["صافي الربح", aiState.facts.netProfit],
            ["رصيد الصندوق", aiState.facts.cash],
          ]
            .map(
              ([label, value]) =>
                `<div class="summary-line"><span>${label}</span><strong>${money(value)}</strong></div>`,
            )
            .join("")}</details>`
        : ""
    }<p class="section-hint">المساعد يشرح ويقترح؛ حفظ الفواتير واعتمادها من شاشات البرنامج بعد مراجعتك. قد يخطئ النموذج الصغير، وتبقى أرقام التقارير هي المرجع.</p><details class="card ai-guidance"><summary>تنبيهات البرنامج وإرشادات البدء</summary><p class="section-hint">${esc(data.answer || "")}</p>${data.alerts?.map((a) => `<p class="notice">${esc(a)}</p>`).join("") || '<p class="section-hint">لا توجد تنبيهات مراجعة حالياً.</p>'}</details>`,
    "المساعد",
  );
}
window.madarAiToken = ({ id, text }) => {
  if (!aiState.running || aiState.requestId !== id) return;
  aiState.answer = text;
  if (route === "assistant" && $("#ai-answer"))
    $("#ai-answer").textContent = text || "جارٍ إعداد الرد…";
};
async function askLocalAi(values) {
  if (aiState.running) return;
  aiState.question = String(values.question || "").trim();
  aiState.days = Number(values.days) || 30;
  aiState.answer = "";
  aiState.error = "";
  aiState.facts = null;
  aiState.running = true;
  aiState.stopped = false;
  const epoch = ++aiState.epoch,
    user = S.user?.id,
    sandbox = !!S.training;
  assistantView();
  try {
    const result = await rpc("ai.ask", {
      question: aiState.question,
      days: aiState.days,
    });
    if (
      aiState.epoch !== epoch ||
      S.user?.id !== user ||
      !!S.training !== sandbox
    )
      return;
    aiState.answer = result.answer;
    aiState.facts = result.facts;
    aiState.model = result.model;
    aiState.stopped = result.stopped;
  } catch (error) {
    if (aiState.epoch === epoch) aiState.error = error.message;
  } finally {
    if (aiState.epoch === epoch) {
      aiState.running = false;
      if (route === "assistant") assistantView();
    }
  }
}
const lessons = [
  [
    "الترحيب بمتجرك",
    "هنا ترى مبيعات اليوم والصندوق وآخر العمليات. بيانات التدريب لا تغيّر بيانات متجرك.",
    "home",
  ],
  [
    "المنتجات والوحدات",
    "افتح منتجاً لتراجع وحدته الأساسية وسعره. الكرتون يمكن أن يحتوي عدة حبات بمعامل تحويل واضح.",
    "inventory",
  ],
  [
    "استلام البضاعة",
    "فاتورة الشراء تزيد المخزون وتثبت تكلفة البضاعة؛ دفعها يقلل الصندوق أو يزيد مستحق المورد.",
    "purchase",
  ],
  [
    "بيع أول فاتورة",
    "اختر المنتج والكمية وطريقة الدفع. يُسجَّل البيع وتكلفة الأصناف داخل معاملة واحدة.",
    "sale",
  ],
  [
    "الصندوق والاستحقاقات",
    "التحصيل يسوّي دين العميل ولا ينشئ بيعاً جديداً. عند الإغلاق قارن النقد المعدود بالمتوقع.",
    "accounts",
  ],
  [
    "قراءة التقارير",
    "المبيعات تختلف عن الربح؛ الربح يتأثر بتكلفة البضاعة والمصروفات. راجع الربحية وميزان المراجعة.",
    "reports",
  ],
  [
    "المراجعة المستمرة",
    "راجع الصلاحية والاستحقاقات وفروق الصندوق، واحتفظ بنسخة احتياطية خارج الهاتف.",
    "assistant",
  ],
];
function trainingView() {
  frame(
    `<section class="intro"><h1>تعلّم بثقة</h1><p>خطوات واضحة دون المساس ببياناتك</p></section><section class="card hero"><div class="row between"><h2>جولة تدريبية للموظف</h2><span class="medallion">${icon("book")}</span></div><p style="margin-top:15px;line-height:2">نتدرّب على المنتجات والشراء والبيع والصندوق والتقارير داخل متجر تدريب منفصل تماماً.</p></section><div class="stack" style="margin-top:18px">${btn(S.training ? "متابعة الشرح" : "ابدأ / استأنف التدريب", "training.start", "btn full", "book")}${S.training ? btn("العودة للمتجر الحقيقي", "training.exit", "btn outline full", "home") : ""}</div><div class="section-title"><h3>ما ستتعلمه</h3></div><div class="card list-card">${lessons.map((l, i) => `<div class="menu-row"><span class="medallion">${i + 1}</span><span><strong>${esc(l[0])}</strong><small>شرح الخطوات والسبب المحاسبي</small></span></div>`).join("")}</div>`,
    "التدريب",
  );
}
function guide() {
  const item = lessons[guideStep];
  return `<aside class="guide" role="region" aria-label="الإرشاد التدريبي"><div class="row"><strong>${esc(item[0])}</strong><button data-action="guide.stop" aria-label="إيقاف الشرح">×</button></div><p>${esc(item[1])}</p><div class="row"><button class="btn light" data-action="guide.previous" ${guideStep === 0 ? "disabled" : ""}>السابق</button><span class="step">${guideStep + 1} / ${lessons.length}</span><button class="btn" data-action="guide.next">${guideStep === lessons.length - 1 ? "إنهاء" : "التالي"}</button></div></aside>`;
}
function productForm(p = null) {
  context.product = p;
  modal(
    p ? "تعديل المنتج" : "إضافة منتج",
    `<form data-form="product">${field("اسم المنتج", "name", p?.name || "", "text", 'required maxlength="150"')}${field("الباركود", "barcode", p?.barcode || "", "text", 'maxlength="80"')}${field("التصنيف", "category", p?.category || "", "text", 'maxlength="80"')}<div class="grid">${field("الوحدة الأساسية", "baseUnit", p?.baseUnit || "حبة", "text", 'required maxlength="50" ' + (p ? "readonly" : ""))}${field("حد تنبيه المخزون", "minimum", (p?.minimum || 0) / 1000, "number", 'required min="0" step="0.001"')}</div>${field("سعر بيع الوحدة الأساسية", "price", (p?.price || 0) / 100, "number", 'required min="0" step="0.01"')}<label class="check-label"><input name="enabled" type="checkbox" ${!p || p.enabled ? "checked" : ""}>منتج نشط</label><p class="section-hint">تدخل الكميات بفاتورة شراء؛ تعديل السعر لا يغيّر تكلفة المخزون القديم.</p>${saveButton(p ? "حفظ التعديل" : "إضافة المنتج")}</form>`,
  );
}
function partyForm(p = null) {
  context.party = p;
  modal(
    p ? "تعديل بيانات الطرف" : "عميل أو مورد جديد",
    `<form data-form="party">${field("الاسم", "name", p?.name || "", "text", 'required maxlength="150"')}${select(
      "نوع الطرف",
      "kind",
      owner()
        ? [
            ["Customer", "عميل"],
            ["Supplier", "مورد"],
          ]
        : [["Customer", "عميل"]],
      p?.kind || "Customer",
      p ? "disabled" : "",
    )}${field("رقم الهاتف", "phone", p?.phone || "", "tel", 'maxlength="50"')}${field("أيام الاستحقاق الافتراضية", "dueDays", p?.dueDays ?? 30, "number", 'min="0" max="3650" step="1" required')}${saveButton()}</form>`,
  );
}
function unitForm(u = null) {
  context.unit = u;
  const p = context.product;
  modal(
    u ? "تعديل سعر الوحدة" : "إضافة وحدة تحويل",
    `<form data-form="unit">${field("اسم الوحدة", "name", u?.name || "", "text", "required " + (u ? "readonly" : ""))}${field("كم تحتوي من الوحدة الأساسية؟", "factor", u ? u.factor / 1000 : 12, "number", 'min="0.001" step="0.001" required ' + (u ? "readonly" : ""))}<p class="section-hint">مثال: كرتون واحد = 12 ${esc(p.baseUnit)}. بعد اعتماد الوحدة يبقى معاملها ثابتاً.</p>${field("سعر بيع هذه الوحدة", "price", u ? u.price / 100 : 0, "number", 'min="0" step="0.01" required')}${saveButton()}</form>`,
  );
}
async function cashForm(kind, party = 0) {
  const choices = ["Receipt", "Payment"].includes(kind)
    ? await rpc("parties", {
        kind: kind === "Receipt" ? "Customer" : "Supplier",
      })
    : [];
  context.cash = { kind, requestId: uuid() };
  modal(
    kinds[kind],
    `<form data-form="cash">${
      choices.length
        ? select(
            "الطرف",
            "partyId",
            choices.map((p) => [
              p.id,
              `${p.name} — ${money(p.balance, false)}`,
            ]),
            party,
          )
        : ["Receipt", "Payment"].includes(kind)
          ? '<p class="notice">أضف الطرف وسجّل فاتورته الآجلة أولاً.</p>'
          : ""
    }${field("المبلغ", "amount", "", "number", 'required min="0.01" max="100000000" step="0.01"')}${field("وصف الحركة", "note", "", "text", 'required maxlength="500"')}${saveButton("حفظ الحركة")}</form>`,
  );
}
function pickerRows(search = "") {
  const matches = data.catalog.items
    .filter(
      (p) =>
        p.enabled && (p.name.includes(search) || p.barcode.includes(search)),
    )
    .slice(0, 100);
  return (
    matches
      .map(
        (p) =>
          `<button class="menu-row" data-action="invoice.choose" data-id="${p.id}"><span class="menu-icon">${icon("cube")}</span><span class="grow"><strong>${esc(p.name)}</strong><small>${esc(p.barcode || "")} · ${quantity(p.quantity)} ${esc(p.baseUnit)}</small></span><strong>${money(p.price, false)}</strong></button>`,
      )
      .join("") || '<p class="empty">لا توجد منتجات مطابقة</p>'
  );
}
function invoicePicker() {
  modal(
    "إضافة صنف",
    `<form data-form="picker.search" class="search"><input name="search" placeholder="اسم المنتج أو باركوده" aria-label="بحث الأصناف"><button class="icon-btn" aria-label="بحث">${icon("search", 18)}</button></form><div id="picker-results">${pickerRows()}</div>`,
  );
}
function addProduct(p) {
  let line = invoice.lines.find((l) => l.productId === p.id);
  if (line) line.quantity += 1;
  else {
    const unit = data.catalog.units.find(
      (u) => u.productId === p.id && u.factor === 1000,
    );
    if (!unit) throw Error("الوحدة الأساسية غير موجودة");
    invoice.lines.push({
      productId: p.id,
      name: p.name,
      unitId: unit.id,
      quantity: 1,
      price:
        invoice.kind === "Sale"
          ? unit.price / 100
          : p.quantity
            ? (p.value * 10) / p.quantity
            : 0,
      batch: "",
      expiry: "",
    });
  }
  invoice.dirty = true;
  closeModal();
  invoiceView();
}
async function addBarcode(code) {
  code = String(code || "").trim();
  if (!code) return;
  let found = data.catalog.items.find((p) => p.barcode === code && p.enabled);
  if (!found) {
    const fetched = await rpc("products", { search: code });
    found = fetched.items.find((p) => p.barcode === code && p.enabled);
    if (found) {
      data.catalog.items.push(found);
      data.catalog.units = fetched.units;
    }
  }
  if (!found) throw Error("الباركود غير موجود أو المنتج معطل");
  addProduct(found);
}
async function documentDetail(id) {
  const d = await rpc("document", { id });
  context.document = d;
  modal(
    `عملية #${id}`,
    `<span class="pill ${d.header.status === "Draft" ? "draft" : ""}">${esc(kinds[d.header.kind] || d.header.kind)} · ${d.header.status === "Draft" ? "مسودة" : "معتمدة"}</span><p class="section-hint">${esc(d.header.partyName || "نقدي")} · ${esc(new Date(d.header.createdAt).toLocaleString("ar"))}<br>الموظف: ${esc(d.header.employee)}</p>${d.lines.map((l) => `<div class="summary-line"><span>${esc(l.name)}<small class="muted"> · ${quantity(l.quantity)} ${esc(l.unitName)}</small></span><strong>${money(l.total)}</strong></div>`).join("")}<div class="summary-line"><span>الإجمالي</span><strong>${money(d.header.total)}</strong></div><div class="summary-line"><span>المدفوع عند الحفظ</span><strong>${money(d.header.paid)}</strong></div><p class="section-hint">${esc(d.header.note)}</p><div class="stack">${d.header.status === "Draft" && writer() ? btn("فتح المسودة للتعديل", "document.edit", "btn full", "receipt") : ""}${btn("طباعة / حفظ PDF", "document.print", "btn outline full", "print")}${writer() ? btn("إضافة مرفق", "document.attach", "btn light full", "folder") : ""}</div>${d.attachments.length ? '<div class="section-title"><h3>المرفقات</h3></div>' + d.attachments.map((a) => btn(a.fileName, "attachment.open", "menu-row", "folder", `data-id="${a.id}"`)).join("") : ""}`,
  );
}
async function draftOpen() {
  const d = context.document;
  closeModal();
  invoice = {
    id: d.header.id,
    requestId: d.header.requestId,
    kind: d.header.kind,
    partyId: d.header.partyId || 0,
    warehouseId: d.header.warehouseId,
    full: d.header.paid === d.header.total,
    paid: d.header.paid / 100,
    dueDate: d.header.dueDate,
    note: d.header.note,
    dirty: false,
    lines: d.lines.map((l) => ({
      productId: l.productId,
      name: l.name,
      unitId: l.unitId,
      quantity: l.quantity / 1000,
      price: l.price / 100,
      batch: l.batch,
      expiry: l.expiry,
    })),
  };
  route = "invoice";
  await invoiceData();
  invoiceView();
}
async function stockForm() {
  const p = context.product;
  const warehouses = data.warehouses;
  const warehouse = inventoryFilter.warehouse || 1;
  const catalog = await rpc("products", {
    warehouse,
    search: p.barcode || p.name,
  });
  const stock = catalog.items.find((row) => row.id === p.id);
  modal(
    "جرد أو تسوية مخزون",
    `<form data-form="stock">${select(
      "المستودع",
      "warehouseId",
      warehouses.map((w) => [w.id, w.name]),
      warehouse,
      'id="adjust-warehouse"',
    )}${field("الكمية الفعلية بالوحدة الأساسية (" + p.baseUnit + ")", "quantity", (stock?.quantity || 0) / 1000, "number", 'required min="0" step="0.001" id="adjust-quantity"')}${field("تكلفة الوحدة الأساسية عند زيادة الكمية", "unitCost", stock?.quantity ? Math.round((stock.value * 1000) / stock.quantity) / 100 : 0, "number", 'required min="0" step="0.01"')}${field("سبب التسوية أو التلف أو الانتهاء", "reason", "", "text", 'required maxlength="500"')}${field("صلاحية الكمية المضافة (اختياري)", "expiry", "", "date")}<p class="notice" style="margin-bottom:15px">هذه تسوية موثقة يؤثر فرقها على المخزون والحسابات. لا تستخدمها بدلاً من فاتورة شراء.</p>${saveButton("اعتماد التسوية")}</form>`,
  );
}
function lotsView() {
  modal(
    "الدفعات والصلاحية",
    data.lots.length
      ? data.lots
          .map(
            (l) =>
              `<section class="card" style="margin-bottom:10px"><div class="row between"><h3>${esc(l.name)}</h3>${l.expiry && l.expiry < today() ? '<span class="pill danger">منتهية</span>' : ""}</div><p class="section-hint">${esc(l.warehouse)} · ${esc(l.batch || "دون رقم دفعة")}<br>المتبقي: ${quantity(l.remaining)} ${esc(l.baseUnit)}<br>الصلاحية: ${l.expiry ? date(l.expiry) : "غير محددة"}</p></section>`,
          )
          .join("")
      : '<p class="empty">لا توجد دفعات مسجلة بعد</p>',
  );
}
function userForm(u = null) {
  context.user = u;
  modal(
    u ? "تعديل حساب الموظف" : "حساب موظف جديد",
    `<form data-form="user">${field("اسم المستخدم", "name", u?.name || "", "text", 'required maxlength="100"')}${select(
      "الصلاحية",
      "role",
      [
        ["Cashier", "كاشير: بيع وتحصيل"],
        ["Viewer", "عرض فقط"],
        ["Owner", "مدير: جميع العمليات"],
      ],
      u?.role || "Cashier",
    )}${field(u ? "رمز جديد (اتركه فارغاً للإبقاء على الحالي)" : "الرمز السري", "pin", "", "password", `${u ? "" : "required "}minlength="6" maxlength="12" inputmode="numeric"`)}<label class="check-label"><input name="enabled" type="checkbox" ${!u || u.enabled ? "checked" : ""}>حساب مفعّل</label>${saveButton()}</form>`,
  );
}
function exportRows(format) {
  const rows = reportRows.map((r) =>
    Object.fromEntries(
      Object.entries(r)
        .filter(([k]) => k !== "productId")
        .map(([k, v]) => [
          columnNames[k] || k,
          cellValue(k, v)
            .replace(/&amp;/g, "&")
            .replace(/&lt;/g, "<")
            .replace(/&gt;/g, ">")
            .replace(/&quot;/g, '"')
            .replace(/&#39;/g, "'"),
        ]),
    ),
  );
  return rpc("files.export", { format, rows });
}
async function navigateGuide(step) {
  guideStep = step;
  await rpc("training.save", {
    lesson: "getting-started",
    step,
    completed: step === lessons.length - 1,
  });
  await go(lessons[step][2], true);
}
const actions = {
  "ai.download": () => rpc("ai.download"),
  "ai.import": async () => {
    if (await rpc("ai.import")) {
      aiState.info = await rpc("ai.status");
      if (route === "assistant") assistantView();
      toast("اكتمل التحقق من النموذج وتجهيزه على الهاتف");
    }
  },
  "ai.remove": async () => {
    if (
      await confirm(
        "حذف ملف الذكاء",
        "يمكن تنزيله مجدداً. بيانات المتجر محفوظة بشكل منفصل. هل تريد الحذف؟",
      )
    ) {
      await rpc("ai.remove");
      aiState.info = await rpc("ai.status");
      aiState.answer = "";
      aiState.facts = null;
      if (route === "assistant") assistantView();
    }
  },
  "ai.cancel": () => rpc("ai.cancel"),

  "modal.close": () => closeModal(),
  "confirm.yes": () => {
    const resolve = confirmResolve;
    confirmResolve = null;
    closeModal();
    resolve?.(true);
  },
  back: () => go("home"),
  logout: async () => {
    if (
      !(await confirm(
        "تسجيل الخروج",
        "احفظ المسودات قبل الخروج. هل تريد المتابعة؟",
      ))
    )
      return;
    resetAiSession();
    await rpc("logout");
    guideStep = -1;
    invoice = null;
    await initialize();
  },
  "product.new": () => productForm(),
  product: (el) => productDetail(Number(el.dataset.id)),
  "product.edit": () => productForm(context.product),
  "unit.new": () => unitForm(),
  "unit.edit": (el) =>
    unitForm(data.units.find((u) => u.id === Number(el.dataset.id))),
  "party.new": () => partyForm(),
  "party.edit": (el) =>
    partyForm(data.find((p) => p.id === Number(el.dataset.id))),
  cash: (el) => cashForm(el.dataset.kind, Number(el.dataset.party || 0)),
  "inventory.all": async () => {
    inventoryFilter.low = false;
    await go("inventory");
  },
  "inventory.low": async () => {
    inventoryFilter.low = true;
    await go("inventory");
  },
  "warehouse.new": () =>
    modal(
      "مستودع جديد",
      `<form data-form="warehouse">${field("اسم المستودع", "name", "", "text", 'required maxlength="100"')}${saveButton()}</form>`,
    ),
  lots: () => lotsView(),
  "stock.adjust": () => stockForm(),
  "invoice.add": () => invoicePicker(),
  "invoice.choose": (el) =>
    addProduct(data.catalog.items.find((p) => p.id === Number(el.dataset.id))),
  "invoice.remove": (el) => {
    invoice.lines.splice(Number(el.dataset.index), 1);
    invoice.dirty = true;
    invoiceView();
  },
  "invoice.minus": (el) => {
    const l = invoice.lines[Number(el.dataset.index)];
    l.quantity = Math.max(0.001, l.quantity - 1);
    invoice.dirty = true;
    invoiceView();
  },
  "invoice.plus": (el) => {
    invoice.lines[Number(el.dataset.index)].quantity += 1;
    invoice.dirty = true;
    invoiceView();
  },
  "invoice.full": () => {
    invoice.full = true;
    invoice.dirty = true;
    invoiceView();
  },
  "invoice.credit": () => {
    invoice.full = false;
    invoice.dirty = true;
    invoiceView();
  },
  "invoice.save": async () => {
    const id = await rpc("invoice.save", invoice);
    invoice.id = id;
    invoice.dirty = false;
    toast("حُفظت المسودة #" + id);
    invoiceView();
  },
  "invoice.post": async () => {
    const total = await rpc("invoice.total", invoice);
    if (
      !(await confirm(
        "اعتماد الفاتورة",
        `الإجمالي ${money(total, false)} ${settings.currency}. سيُحدَّث المخزون والحسابات، ولا تُعدَّل الفاتورة المعتمدة. هل تريد الاعتماد؟`,
      ))
    )
      return;
    const id = await rpc("invoice.post", invoice);
    invoice.dirty = false;
    invoice = null;
    await loadState();
    await go("documents", true);
    toast("اعتُمدت الفاتورة #" + id);
  },
  barcode: async () => {
    const code = await rpc("barcode.photo");
    if (code) await addBarcode(code);
  },
  document: (el) => documentDetail(Number(el.dataset.id)),
  "document.edit": () => draftOpen(),
  "document.print": () =>
    rpc("files.print", { id: context.document.header.id }),
  "document.attach": async () => {
    const id = context.document.header.id;
    await rpc("files.attach", { documentId: id });
    await documentDetail(id);
  },
  "attachment.open": (el) => rpc("files.open", { id: Number(el.dataset.id) }),
  "documents.filter": async (el) => {
    data = await rpc("documents", { status: el.dataset.status });
    documentsView();
  },
  "reports.debts": async () => {
    reportType = "debts";
    await go("reports");
  },
  "export.xlsx": () => exportRows("xlsx"),
  "export.csv": () => exportRows("csv"),
  "shift.open": async () => {
    if (
      !(await confirm(
        "فتح وردية",
        "ستُفتح الوردية باسمك وبرصيد الصندوق المسجّل. هل تريد المتابعة؟",
      ))
    )
      return;
    await rpc("shift.open");
    await go("accounts", true);
    toast("فُتحت الوردية");
  },
  "shift.close": () =>
    modal(
      "إغلاق وعدّ الصندوق",
      `<form data-form="shift.close">${field("النقد الفعلي المعدود", "counted", "", "number", 'required min="0" step="0.01"')}${field("سبب الفرق (عند وجوده)", "reason", "", "text", 'maxlength="500"')}<p class="section-hint">أي فرق يحتاج مراجعة المدير قبل فتح وردية أخرى.</p>${saveButton("تسجيل الإغلاق")}</form>`,
    ),
  "shift.approve": async (el) => {
    const rows = await rpc("shifts");
    const s = rows.find((s) => s.id === Number(el.dataset.id));
    context.shift = s;
    modal(
      "مراجعة فرق الصندوق",
      `<div class="summary-line"><span>المتوقع</span><strong>${money(s.expectedCash)}</strong></div><div class="summary-line"><span>الفعلي</span><strong>${money(s.countedCash)}</strong></div><p class="section-hint">السبب المسجل: ${esc(s.reason)}</p><form data-form="shift.approve">${field("سبب اعتماد التسوية", "reason", "", "text", 'required maxlength="500"')}${saveButton("اعتماد وتسوية الفرق")}</form>`,
    );
  },
  "user.new": () => userForm(),
  "user.edit": (el) =>
    userForm(data.find((u) => u.id === Number(el.dataset.id))),
  "backup.create": () => rpc("files.backup"),
  "backup.restore": async () => {
    if (
      !(await confirm(
        "استعادة نسخة احتياطية",
        "ستُستبدل البيانات الحالية بالنسخة المختارة بعد التحقق منها، وسيُطلب تسجيل الدخول من جديد. تُحفظ نسخة من البيانات الحالية أولاً. هل تريد اختيار النسخة؟",
      ))
    )
      return;
    if (await rpc("files.restore")) {
      resetAiSession();
      guideStep = -1;
      await initialize();
      toast("اكتملت الاستعادة. سجّل الدخول ببيانات النسخة المستعادة.");
    }
  },
  "import.template": () =>
    rpc("files.export", {
      format: "xlsx",
      rows: [
        {
          الاسم: "مثال منتج",
          الباركود: "1001",
          الوحدة: "حبة",
          التصنيف: "مواد غذائية",
          السعر: "5.00",
          "حد المخزون": "2",
        },
      ],
    }),
  "import.preview": async () => {
    const result = await rpc("files.importPreview");
    if (!result) return;
    context.import = result;
    const p = result.preview;
    modal(
      "مراجعة الاستيراد",
      `<p class="section-hint">${p.rows.length} منتجات صالحة، و${p.errors.length} أخطاء. لن يُعتمد الملف عند وجود أخطاء.</p>${p.errors.length ? `<div class="notice error">${p.errors.slice(0, 20).map(esc).join("<br>")}</div>` : ""}<div class="stack">${p.rows
        .slice(0, 5)
        .map(
          (r) =>
            `<div class="summary-line"><span>${esc(r.name)} · ${esc(r.barcode || "")}</span><span>${esc(r.baseUnit)}</span></div>`,
        )
        .join(
          "",
        )}</div>${p.errors.length ? "" : btn("اعتماد الاستيراد", "import.apply", "btn full", "check", "data-write")}`,
    );
  },
  "import.apply": async () => {
    const count = await rpc("files.importApply", {
      token: context.import.token,
    });
    closeModal();
    toast(`استُورد ${count} منتجاً`);
    await go("inventory", true);
  },
  "training.start": async () => {
    if (!S.training) await rpc("training.enter");
    await loadState();
    const progress = (await rpc("training")).find(
      (l) => l.lesson === "getting-started",
    );
    await navigateGuide(
      progress && !progress.completed
        ? Math.min(progress.step, lessons.length - 1)
        : 0,
    );
  },
  "training.exit": async () => {
    if (
      !(await confirm(
        "إنهاء التدريب",
        "ستعود إلى بيانات متجرك الحقيقية. هل تريد إنهاء وضع التدريب؟",
      ))
    )
      return;
    await rpc("training.exit");
    guideStep = -1;
    invoice = null;
    await loadState();
    await go("home", true);
  },
  "guide.stop": () => {
    guideStep = -1;
    frameRefresh();
  },
  "guide.previous": () => navigateGuide(Math.max(0, guideStep - 1)),
  "guide.next": async () => {
    if (guideStep === lessons.length - 1) {
      await rpc("training.save", {
        lesson: "getting-started",
        step: guideStep,
        completed: true,
      });
      guideStep = -1;
      await go("training", true);
      toast("أكملت الجولة. يمكنك متابعة التجربة أو العودة للمتجر الحقيقي.");
    } else await navigateGuide(guideStep + 1);
  },
};
function frameRefresh() {
  if (route === "invoice") invoiceView();
  else go(route, true);
}
document.addEventListener("click", (event) => {
  const element = event.target.closest("[data-action],[data-go]");
  if (!element || element.disabled) return;
  event.preventDefault();
  const action = element.dataset.action;
  if (action === "modal.close" || action === "confirm.yes") {
    actions[action](element);
    return;
  }
  if (busy && element.hasAttribute("data-write")) return;
  work(async () => {
    if (element.dataset.go) await go(element.dataset.go);
    else if (actions[action]) await actions[action](element);
  });
});
document.addEventListener("submit", (event) => {
  const form = event.target;
  if (!form.dataset.form) return;
  event.preventDefault();
  const v = Object.fromEntries(new FormData(form));
  if (form.dataset.form === "assistant.ai") {
    void askLocalAi(v);
    return;
  }
  work(async () => {
    switch (form.dataset.form) {
      case "login":
        await rpc("login", {
          name: v.name,
          pin: v.pin.replace(/[٠-٩]/g, (c) => String(c.charCodeAt(0) - 1632)),
        });
        await initialize();
        break;
      case "bootstrap":
        if (v.pin !== v.confirm) throw Error("الرمزان غير متطابقين");
        await rpc("bootstrap", {
          name: v.name,
          pin: v.pin.replace(/[٠-٩]/g, (c) => String(c.charCodeAt(0) - 1632)),
          storeName: v.storeName,
          currency: v.currency,
        });
        await initialize();
        break;
      case "product":
        await rpc("products.save", {
          id: context.product?.id || 0,
          name: v.name,
          barcode: v.barcode,
          category: v.category,
          baseUnit: v.baseUnit,
          minimum: num(v.minimum),
          price: num(v.price),
          enabled: !!v.enabled,
        });
        closeModal();
        await go("inventory", true);
        toast("حُفظ المنتج");
        break;
      case "unit":
        await rpc("units.save", {
          id: context.unit?.id || 0,
          productId: context.product.id,
          name: v.name,
          factor: num(v.factor),
          price: num(v.price),
        });
        closeModal();
        data = await rpc("products", inventoryFilter);
        await productDetail(context.product.id);
        toast("حُفظت الوحدة وسعرها");
        break;
      case "party":
        await rpc("parties.save", {
          id: context.party?.id || 0,
          name: v.name,
          kind: context.party?.kind || v.kind,
          phone: v.phone,
          dueDays: num(v.dueDays),
        });
        closeModal();
        await go("parties", true);
        toast("حُفظ الطرف");
        break;
      case "cash":
        await rpc("cash.post", {
          ...context.cash,
          amount: num(v.amount),
          partyId: num(v.partyId || 0),
          note: v.note,
        });
        closeModal();
        await go("accounts", true);
        toast("حُفظت الحركة");
        break;
      case "warehouse":
        await rpc("warehouses.save", { name: v.name });
        closeModal();
        await go("inventory", true);
        toast("أُضيف المستودع");
        break;
      case "inventory.search":
        inventoryFilter.search = v.search;
        await go("inventory", true);
        break;
      case "invoice.barcode":
        await addBarcode(v.barcode);
        break;
      case "picker.search": {
        const found = await rpc("products", { search: v.search });
        data.catalog = found;
        $("#picker-results").innerHTML = pickerRows();
        break;
      }
      case "stock":
        await rpc("stock.adjust", {
          productId: context.product.id,
          warehouseId: num(v.warehouseId),
          quantity: num(v.quantity),
          unitCost: num(v.unitCost),
          reason: v.reason,
          expiry: v.expiry,
        });
        closeModal();
        await go("inventory", true);
        toast("اعتُمدت التسوية");
        break;
      case "shift.close": {
        const result = await rpc("shift.close", {
          counted: num(v.counted),
          reason: v.reason,
        });
        closeModal();
        await go("accounts", true);
        toast(
          result.needsApproval
            ? "سُجّل الفرق وبانتظار اعتماد المدير"
            : "أُغلقت الوردية",
        );
        break;
      }
      case "shift.approve":
        await rpc("shift.approve", { id: context.shift.id, reason: v.reason });
        closeModal();
        await go("accounts", true);
        toast("اعتُمدت تسوية الفرق");
        break;
      case "settings":
        await rpc("settings.save", v);
        await loadState();
        settingsView();
        toast("حُفظت الإعدادات");
        break;
      case "user":
        await rpc("users.save", {
          id: context.user?.id || 0,
          name: v.name,
          role: v.role,
          pin: v.pin,
          enabled: !!v.enabled,
        });
        closeModal();
        await go("users", true);
        toast("حُفظ الحساب");
        break;
      case "report":
        reportType = v.kind;
        context.reportFrom = v.from;
        context.reportTo = v.to;
        reportRows = await rpc("reports", {
          kind: v.kind,
          from: v.from,
          to: v.to,
        });
        reportsView();
        break;
      case "assistant":
        data = await rpc("assistant", { question: v.question });
        assistantView();
        break;
    }
  });
});
document.addEventListener("change", (event) => {
  const el = event.target;
  work(async () => {
    if (el.id === "warehouse-filter") {
      inventoryFilter.warehouse = num(el.value);
      await go("inventory", true);
    }
    if (el.id === "category-filter") {
      inventoryFilter.category = el.value;
      await go("inventory", true);
    }
    if (el.id === "adjust-warehouse") {
      const catalog = await rpc("products", {
        warehouse: num(el.value),
        search: context.product.name,
      });
      const p = catalog.items.find((p) => p.id === context.product.id);
      $("#adjust-quantity").value = (p?.quantity || 0) / 1000;
    }
    if (route !== "invoice" || !invoice) return;
    if (el.id === "invoice-party") {
      invoice.partyId = num(el.value);
      invoice.dirty = true;
    }
    if (el.id === "invoice-warehouse") {
      invoice.warehouseId = num(el.value);
      invoice.dirty = true;
    }
    if (el.id === "invoice-paid") {
      invoice.paid = num(el.value);
      invoice.dirty = true;
    }
    if (el.id === "invoice-due") {
      invoice.dueDate = el.value;
      invoice.dirty = true;
    }
    if (el.hasAttribute("data-line-unit")) {
      const l = invoice.lines[num(el.dataset.lineUnit)],
        u = data.catalog.units.find((u) => u.id === num(el.value));
      const oldUnit = data.catalog.units.find((unit) => unit.id === l.unitId);
      if (invoice.kind === "Purchase" && oldUnit)
        l.price =
          Math.round(((l.price * u.factor) / oldUnit.factor) * 100) / 100;
      l.unitId = u.id;
      if (invoice.kind === "Sale") l.price = u.price / 100;
      invoice.dirty = true;
      invoiceView();
    }
    if (el.hasAttribute("data-line-quantity")) {
      const value = num(el.value);
      if (value <= 0) throw Error("الكمية يجب أن تكون موجبة");
      invoice.lines[num(el.dataset.lineQuantity)].quantity = value;
      invoice.dirty = true;
      invoiceView();
    }
    if (el.hasAttribute("data-line-price")) {
      const value = num(el.value);
      if (value < 0) throw Error("السعر لا يكون سالباً");
      invoice.lines[num(el.dataset.linePrice)].price = value;
      invoice.dirty = true;
      invoiceView();
    }
    if (el.hasAttribute("data-line-batch")) {
      invoice.lines[num(el.dataset.lineBatch)].batch = el.value;
      invoice.dirty = true;
    }
    if (el.hasAttribute("data-line-expiry")) {
      invoice.lines[num(el.dataset.lineExpiry)].expiry = el.value;
      invoice.dirty = true;
    }
  });
});
document.addEventListener("input", (event) => {
  if (event.target.id === "invoice-note" && invoice) {
    invoice.note = event.target.value;
    invoice.dirty = true;
  }
});
dialog.addEventListener("cancel", (event) => {
  event.preventDefault();
  closeModal();
});
window.madarBack = () => {
  if (dialog.open) return closeModal();
  if (route !== "home" && route !== "auth") return work(() => go("home"));
  if (Date.now() - lastBack < 1800) return rpc("app.exit");
  lastBack = Date.now();
  toast("اضغط رجوع مرة أخرى للخروج");
};
window.visualViewport?.addEventListener("resize", () =>
  document.body.classList.toggle(
    "keyboard",
    window.visualViewport.height < window.innerHeight * 0.75,
  ),
);
work(initialize);
