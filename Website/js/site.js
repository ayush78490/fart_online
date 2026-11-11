// Fart Online website: download sizes, live server status, "best for you" card and the trailer popup.
(function () {
  "use strict";

  function mb(bytes) { return (bytes / 1048576).toFixed(bytes < 10485760 ? 1 : 0) + " MB"; }

  // 0. Account emails: Supabase sends people back here (its "Site URL") with the result in the address after "#".
  //    Show what happened, then wipe the hash so the login tokens don't stay in the address bar or history.
  (function authResult() {
    var hash = location.hash.replace(/^#/, "");
    if (!/(^|&)(access_token|error|error_code)=/.test(hash)) return;
    var q = {};
    hash.split("&").forEach(function (kv) { var i = kv.indexOf("="); if (i > 0) q[kv.slice(0, i)] = decodeURIComponent(kv.slice(i + 1).replace(/\+/g, " ")); });
    history.replaceState(null, "", location.pathname + location.search);
    var dlg = document.querySelector(".notice");
    var title, text, ok = !q.error && !q.error_code;
    if (ok && (q.type === "signup" || q.type === "email_change" || q.type === "invite" || !q.type)) {
      title = "Email confirmed!"; text = "Your Fart Online account is ready. Go back to the game and log in with your email and password.";
    } else if (ok && q.type === "recovery") {
      title = "Link accepted"; text = "Open the game and log in. If you forgot your password, ask the admin to reset it for now.";
    } else if (q.error_code === "otp_expired") {
      title = "That link has expired"; text = "Confirmation links work once and only for a limited time. Enter your email and we'll send a fresh one. Open the newest email.";
    } else {
      title = "Something went wrong"; text = (q.error_description || q.error || "The email link didn't work.") + " You can ask for a new link below.";
    }
    if (!ok) setupResend(dlg.querySelector("[data-resend]"));
    dlg.classList.toggle("notice--error", !ok);
    dlg.querySelector("[data-notice-icon]").textContent = ok ? "✓" : "!";
    dlg.querySelector("[data-notice-title]").textContent = title;
    dlg.querySelector("[data-notice-text]").textContent = text;
    if (typeof dlg.showModal === "function") dlg.showModal(); else alert(title + "\n\n" + text);
  })();

  // Resend the sign-up confirmation through Supabase Auth. The anon key is the PUBLIC key that also ships inside
  // every game build; Supabase rate-limits this endpoint per address.
  function setupResend(form) {
    var SUPABASE_URL = "https://qsvyhyxlnxizsxbifvrq.supabase.co";
    var ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InFzdnloeXhsbnhpenN4YmlmdnJxIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTQ0MDM1MTMsImV4cCI6MjEwOTk3OTUxM30.DvTOhK7hYFPaEyZzlK-Um3WrP1cc-ZrdfDBIqtK86Ik";
    var msg = form.querySelector("[data-resend-msg]");
    form.hidden = false;
    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var btn = form.querySelector("button"); btn.disabled = true; msg.textContent = "Sending…";
      fetch(SUPABASE_URL + "/auth/v1/resend", {
        method: "POST",
        headers: { "Content-Type": "application/json", apikey: ANON_KEY, Authorization: "Bearer " + ANON_KEY },
        body: JSON.stringify({ type: "signup", email: form.email.value.trim() })
      }).then(function (r) { return r.json().catch(function () { return {}; }).then(function (j) { return { ok: r.ok, j: j }; }); })
        .then(function (res) {
          msg.textContent = res.ok ? "Sent! Check your inbox (and spam) for the newest email."
            : (res.j.msg || res.j.error_description || res.j.message || "Couldn't send it. Try again in a minute.");
          btn.disabled = false;
        })
        .catch(function () { msg.textContent = "Couldn't reach the account server. Try again."; btn.disabled = false; });
    });
  }

  // 1. Downloads: sizes and version come from downloads/info.json (written when the site is deployed).
  //    A file missing from the list turns its button into "Coming soon".
  fetch("downloads/info.json", { cache: "no-store" })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (info) {
      if (!info) return;
      document.querySelectorAll("[data-file]").forEach(function (btn) {
        var f = info.files && info.files[btn.getAttribute("data-file")];
        if (f) {
          var s = btn.querySelector("[data-size]");
          if (s) s.textContent = mb(f.size);
        } else {
          btn.classList.add("btn--off");
          btn.removeAttribute("href");
          btn.removeAttribute("download");
          btn.setAttribute("aria-disabled", "true");
          btn.textContent = "Coming soon";
        }
      });
      if (info.version) {
        var v = document.querySelector("[data-version]");
        v.textContent = "Version " + info.version + (info.built ? ", built " + info.built : "") + ".";
        v.hidden = false;
      }
    })
    .catch(function () {});

  // 2. Live status of the online servers (the room service's health check).
  var status = document.querySelector("[data-status]");
  function setStatus(ok, text) {
    status.hidden = false;
    status.classList.toggle("status--down", !ok);
    status.querySelector("[data-status-text]").textContent = text;
  }
  fetch("/api/health", { cache: "no-store" })
    .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
    .then(function (h) {
      if (!h || !h.ok) throw 0;
      var n = h.players || 0;
      setStatus(true, n > 0 ? "Servers online · " + n + (n === 1 ? " pig" : " pigs") + " rafting now" : "Servers online");
    })
    .catch(function () { setStatus(false, "Online play is down right now (solo still works)"); });

  // 3. Highlight the download that fits this device.
  var ua = navigator.userAgent || "";
  var best = /Android/i.test(ua) ? "android" : /iPhone|iPad|iPod/i.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1) ? "web" : /Windows/i.test(ua) ? "windows" : "web";
  var card = document.querySelector('.card[data-platform="' + best + '"]');
  if (card) {
    card.classList.add("card--best");
    card.querySelector(".card__badge").hidden = false;
    card.parentNode.insertBefore(card, card.parentNode.firstElementChild);
  }

  // 4. Trailer popup: the vertical cut on tall screens, the wide one elsewhere.
  var dialog = document.querySelector(".trailer");
  var video = dialog.querySelector("video");
  document.querySelectorAll("[data-trailer]").forEach(function (b) {
    b.addEventListener("click", function () {
      var tall = window.innerHeight > window.innerWidth;
      var src = tall ? "assets/video/trailer-vertical.mp4" : "assets/video/trailer.mp4";
      dialog.classList.toggle("trailer--tall", tall);
      if (video.getAttribute("src") !== src) video.setAttribute("src", src);
      if (typeof dialog.showModal === "function") dialog.showModal(); else window.open(src, "_blank");
      var p = video.play(); if (p && p.catch) p.catch(function () {});
    });
  });
  dialog.addEventListener("close", function () { video.pause(); });
  dialog.addEventListener("click", function (e) { if (e.target === dialog) dialog.close(); });

  // Keep the background clip from running when it can't be seen.
  var bg = document.querySelector(".hero__video");
  if (bg && "IntersectionObserver" in window) {
    new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { if (en.isIntersecting) { var p = bg.play(); if (p && p.catch) p.catch(function () {}); } else bg.pause(); });
    }).observe(bg);
  }
  if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches && bg) { bg.removeAttribute("autoplay"); bg.pause(); }
})();
