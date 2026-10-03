/* ValTrainer website: latest-release links, lazy clips, scroll reveal. No dependencies, no tracking. */
(function () {
  "use strict";

  var REPO = "idoraz1/ValTrainer";
  var API = "https://api.github.com/repos/" + REPO + "/releases/latest";
  var DOWNLOAD_PREFIX = "https://github.com/" + REPO + "/releases/download/";
  var CACHE_KEY = "vt-latest-release-v1";
  var CACHE_MS = 60 * 60 * 1000;
  var reduceMotion = false;
  try { reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches; } catch (e) { /* old browser */ }

  function $all(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  /* ---------- Latest release: version, sizes, date and direct download links ---------- */
  function readCache() {
    try {
      var c = JSON.parse(sessionStorage.getItem(CACHE_KEY) || "null");
      if (c && c.d && Date.now() - c.t < CACHE_MS) return c.d;
    } catch (e) { /* storage blocked */ }
    return null;
  }

  function writeCache(d) {
    try { sessionStorage.setItem(CACHE_KEY, JSON.stringify({ t: Date.now(), d: d })); } catch (e) { /* storage blocked */ }
  }

  function slim(j) {
    // Keep only what the page needs, and only files from this project's releases.
    var assets = [];
    (j.assets || []).forEach(function (a) {
      if (a && typeof a.name === "string" && typeof a.browser_download_url === "string" &&
          a.browser_download_url.indexOf(DOWNLOAD_PREFIX) === 0) {
        assets.push({ name: a.name, size: Number(a.size) || 0, url: a.browser_download_url });
      }
    });
    var page = typeof j.html_url === "string" && j.html_url.indexOf("https://github.com/" + REPO + "/") === 0 ? j.html_url : null;
    return { tag: String(j.tag_name || ""), date: String(j.published_at || ""), page: page, assets: assets };
  }

  function fetchRelease() {
    var cached = readCache();
    if (cached) return Promise.resolve(cached);
    if (!window.fetch) return Promise.reject(new Error("no fetch"));
    return fetch(API, { headers: { Accept: "application/vnd.github+json" }, credentials: "omit" })
      .then(function (r) { if (!r.ok) throw new Error("HTTP " + r.status); return r.json(); })
      .then(function (j) { var d = slim(j); writeCache(d); return d; });
  }

  function mb(bytes) { return Math.round(bytes / 1048576) + " MB"; }

  function find(assets, re) {
    for (var i = 0; i < assets.length; i++) if (re.test(assets[i].name)) return assets[i];
    return null;
  }

  function applyRelease(d) {
    if (!/^v?\d+\.\d+\.\d+/.test(d.tag)) return;
    var version = d.tag.replace(/^v/, "");
    var files = {
      setup: find(d.assets, /-Setup\.exe$/i),
      portable: find(d.assets, /-Portable\.zip$/i),
      sums: find(d.assets, /^SHA256SUMS\.txt$/i)
    };
    Object.keys(files).forEach(function (k) {
      var f = files[k];
      if (!f) return;
      $all('[data-dl="' + k + '"]').forEach(function (a) { a.href = f.url; });
      $all('[data-size="' + k + '"]').forEach(function (el) { if (f.size) el.textContent = mb(f.size); });
      $all('[data-filename="' + k + '"]').forEach(function (el) { el.textContent = f.name; });
    });
    $all("[data-version-label]").forEach(function (el) { el.textContent = "v" + version; });
    if (d.page) $all("[data-release-link]").forEach(function (a) { a.href = d.page; });
    var when = new Date(d.date);
    if (!isNaN(when.getTime())) {
      $all("time[data-date]").forEach(function (t) {
        t.setAttribute("datetime", d.date.slice(0, 10));
        t.textContent = when.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
      });
    }
  }

  try {
    fetchRelease().then(applyRelease).catch(function () { /* keep the static fallback links */ });
  } catch (e) { /* keep the static fallback links */ }

  /* ---------- Clips: load when near the viewport, play while visible, pausable ---------- */
  var ICONS =
    '<svg class="i-pause" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><rect x="3" y="2" width="3.5" height="12"/><rect x="9.5" y="2" width="3.5" height="12"/></svg>' +
    '<svg class="i-play" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M4 2l10 6-10 6z"/></svg>';

  function setupClip(frame) {
    var video = frame.querySelector("video");
    if (!video) return null;
    var srcs = [["webm", frame.getAttribute("data-webm")], ["mp4", frame.getAttribute("data-mp4")]].filter(function (s) { return s[1]; });
    if (!srcs.length) return null;

    var state = { loaded: false, visible: false, userPaused: reduceMotion, broken: false };
    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "clip-toggle";
    btn.innerHTML = ICONS;
    frame.appendChild(btn);

    function label() {
      var playing = !state.userPaused;
      btn.setAttribute("aria-pressed", playing ? "true" : "false");
      btn.setAttribute("aria-label", playing ? "Pause clip" : "Play clip");
    }

    function broken() {
      state.broken = true;
      frame.classList.remove("is-playing");
      btn.remove();
    }

    function load() {
      if (state.loaded) return;
      state.loaded = true;
      srcs.forEach(function (s, i) {
        var el = document.createElement("source");
        el.src = s[1];
        el.type = "video/" + s[0];
        if (i === srcs.length - 1) el.addEventListener("error", broken);
        video.appendChild(el);
      });
      video.load();
    }

    function update() {
      if (state.broken) return;
      if (state.visible && !state.userPaused) {
        load();
        var p = video.play();
        if (p && p.catch) p.catch(function () { /* autoplay blocked: poster stays */ });
      } else if (!video.paused) {
        video.pause();
      }
    }

    video.addEventListener("playing", function () { frame.classList.add("is-playing"); });
    video.addEventListener("error", broken);
    btn.addEventListener("click", function () {
      state.userPaused = !state.userPaused;
      label();
      update();
    });
    label();
    return { frame: frame, state: state, update: update };
  }

  var clips = $all(".frame[data-webm], .frame[data-mp4]").map(setupClip).filter(Boolean);

  if ("IntersectionObserver" in window) {
    var clipIO = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        var c = en.target._clip;
        if (!c) return;
        c.state.visible = en.isIntersecting;
        c.update();
      });
    }, { rootMargin: "200px 0px", threshold: 0.15 });
    clips.forEach(function (c) { c.frame._clip = c; clipIO.observe(c.frame); });
  } else {
    clips.forEach(function (c) { c.state.visible = true; c.update(); });
  }

  document.addEventListener("visibilitychange", function () {
    clips.forEach(function (c) {
      var v = c.frame.querySelector("video");
      if (document.hidden) { if (v && !v.paused) v.pause(); } else { c.update(); }
    });
  });

  /* ---------- Scroll reveal ---------- */
  var reveals = $all(".reveal");
  if (reduceMotion || !("IntersectionObserver" in window)) {
    reveals.forEach(function (el) { el.classList.add("in"); });
  } else {
    var revealIO = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) { en.target.classList.add("in"); revealIO.unobserve(en.target); }
      });
    }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });
    reveals.forEach(function (el) { revealIO.observe(el); });
  }

  /* ---------- Copy buttons ---------- */
  $all("[data-copy]").forEach(function (btn) {
    btn.addEventListener("click", function () {
      var src = document.getElementById(btn.getAttribute("data-copy"));
      if (!src || !navigator.clipboard) return;
      var old = btn.textContent;
      navigator.clipboard.writeText(src.textContent.trim()).then(function () {
        btn.textContent = "Copied";
        setTimeout(function () { btn.textContent = old; }, 1600);
      }).catch(function () { /* clipboard blocked */ });
    });
  });
})();
