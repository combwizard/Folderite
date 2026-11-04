/* Click a picture in the docs to see it larger. A click anywhere, the close button or Esc closes it.
   On a phone it shows the picture at its own size and you pan across it, with the close button staying put. Only pictures that would show bigger are offered: one already shown at its full size is left alone, and so is
   one inside a link (the link wins). A picture can opt out with {data-no-zoom} (attr_list). The page keeps its
   scroll position, and focus goes back to the picture. A hidden theme variant (#only-dark in light mode) is never
   offered, and the check is repeated when the system theme flips. Styles are in stylesheets/extra.css (fl-lightbox). */
(function () {
  "use strict";

  var box = null, opener = null;

  function inPage(img) {
    return !!img.closest(".md-content .md-typeset") && !img.closest("a, .fl-lightbox") && !img.hasAttribute("data-no-zoom");
  }

  /* Whether the picture is shown smaller than it is, so enlarging it shows something. A hidden one (width 0) never is. */
  function worthIt(img) {
    if (!img.clientWidth) return false;
    return img.naturalWidth > img.clientWidth + 16 || img.naturalHeight > img.clientHeight + 16;
  }

  function unmark(img) {
    if (!img.classList.contains("fl-zoomable")) return;
    img.classList.remove("fl-zoomable");
    img.removeAttribute("tabindex");
    img.removeAttribute("role");
    img.removeAttribute("aria-haspopup");
    if (img.title === "Click to enlarge") img.removeAttribute("title");
  }

  function mark(img) {
    if (!inPage(img) || !worthIt(img)) { unmark(img); return; }
    img.classList.add("fl-zoomable");
    img.tabIndex = 0;
    img.setAttribute("role", "button");
    img.setAttribute("aria-haspopup", "dialog");
    img.title = "Click to enlarge";
  }

  function markAll() {
    document.querySelectorAll(".md-content .md-typeset img").forEach(function (img) {
      if (img.complete) mark(img);
      else img.addEventListener("load", function () { mark(img); }, { once: true });
    });
  }

  function close() {
    if (!box) return;
    var gone = box;
    box = null;
    document.documentElement.classList.remove("fl-lightbox-open");
    gone.classList.remove("on");
    setTimeout(function () { gone.remove(); }, 200);
    if (opener && document.contains(opener)) opener.focus({ preventScroll: true });
    opener = null;
  }

  function open(img) {
    close();
    opener = img;
    var alt = img.getAttribute("alt") || "";
    box = document.createElement("div");
    box.className = "fl-lightbox";
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    box.setAttribute("aria-label", alt || "Enlarged picture");

    var fig = document.createElement("figure");
    var big = document.createElement("img");
    big.src = img.currentSrc || img.src;
    big.alt = alt;
    fig.appendChild(big);
    if (alt) {
      var cap = document.createElement("figcaption");
      cap.textContent = alt;
      fig.appendChild(cap);
    }

    var x = document.createElement("button");
    x.type = "button";
    x.className = "fl-lightbox-x";
    x.setAttribute("aria-label", "Close");
    x.innerHTML = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';

    /* The picture scrolls in its own layer. The close button stays outside it, so panning never moves it. */
    var pan = document.createElement("div");
    pan.className = "fl-lightbox-pan";
    pan.appendChild(fig);

    box.append(pan, x);
    box.addEventListener("click", close);
    document.body.appendChild(box);
    document.documentElement.classList.add("fl-lightbox-open");
    void box.offsetWidth;                                           // lay it out at opacity 0 first, so it fades in
    box.classList.add("on");
    x.focus({ preventScroll: true });
  }

  document.addEventListener("click", function (e) {
    var img = e.target;
    if (img && img.classList && img.classList.contains("fl-zoomable") && e.button === 0) open(img);
  });

  document.addEventListener("keydown", function (e) {
    if (box) {
      if (e.key === "Escape") { e.preventDefault(); close(); }
      else if (e.key === "Tab") e.preventDefault();                 // the close button is the only stop
      return;
    }
    var img = e.target;
    if ((e.key === "Enter" || e.key === " ") && img && img.classList && img.classList.contains("fl-zoomable")) {
      e.preventDefault();
      open(img);
    }
  });

  /* The system theme flipped: the other picture variant is now the visible one, so close an open picture and mark again.
     (The palette script listens first, so the attribute has already changed by the time this runs.) */
  if (window.matchMedia) {
    var dark = matchMedia("(prefers-color-scheme: dark)");
    var onTheme = function () { close(); markAll(); };
    if (dark.addEventListener) dark.addEventListener("change", onTheme);
    else if (dark.addListener) dark.addListener(onTheme);
  }

  /* Material's instant navigation swaps the page without a reload: mark the new page's pictures, drop an open one. */
  if (window.document$ && window.document$.subscribe) {
    window.document$.subscribe(function () { close(); markAll(); });
  } else if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", markAll);
  } else {
    markAll();
  }
})();
