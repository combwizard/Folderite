/* Folderite landing page: scroll progress, parallax, pointer effects, reveal, release version, FAQ links. */
(function () {
  'use strict';

  const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const FINE = matchMedia('(hover: hover) and (pointer: fine)').matches;

  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  // ---- Scroll: progress bar, header state, parallax (one coalesced rAF) ----

  function updateProgress(bar, top) {
    const max = document.documentElement.scrollHeight - innerHeight;
    const p = max > 0 ? Math.min(1, Math.max(0, scrollY / max)) : 0;
    if (bar) bar.style.setProperty('--p', p.toFixed(4));
    if (top) top.classList.toggle('scrolled', scrollY > 8);
  }

  // Offsets each [data-depth] element from its layout position (CSS applies --py via `translate`).
  function updateParallax(nodes) {
    const vh = innerHeight;
    nodes.forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.bottom < -vh * 1.5 || r.top > vh * 2.5) return;   // far off screen, skip
      const depth = parseFloat(el.dataset.depth) || 0;
      const cur = el._py || 0;
      const delta = (r.top + r.height / 2 - cur) - vh / 2;
      const py = Math.round(-delta * depth * 10) / 10;
      if (py === cur) return;
      el.style.setProperty('--py', py + 'px');
      el._py = py;
    });
  }

  function scrollEffects() {
    const bar = $('.top .progress');
    const top = $('.top');
    const nodes = REDUCE ? [] : $$('[data-depth]');
    let queued = false;
    function frame() {
      queued = false;
      updateProgress(bar, top);
      if (nodes.length) updateParallax(nodes);
    }
    function schedule() {
      if (!queued) { queued = true; requestAnimationFrame(frame); }
    }
    addEventListener('scroll', schedule, { passive: true });
    addEventListener('resize', schedule);
    frame();
  }

  // ---- Pointer effects ----

  function heroSpotlight() {
    const hero = $('#hero');
    if (!hero || !FINE || REDUCE) return;
    hero.addEventListener('pointermove', (e) => {
      const r = hero.getBoundingClientRect();
      hero.style.setProperty('--mx', (e.clientX - r.left) + 'px');
      hero.style.setProperty('--my', (e.clientY - r.top) + 'px');
    });
  }

  function tilt() {
    if (!FINE || REDUCE) return;
    $$('.tilt').forEach((el) => {
      el.addEventListener('pointermove', (e) => {
        const r = el.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width;
        const y = (e.clientY - r.top) / r.height;
        el.classList.add('tilting');
        el.style.setProperty('--ry', ((x - 0.5) * 10).toFixed(2) + 'deg');   // right edge = +5
        el.style.setProperty('--rx', ((0.5 - y) * 8).toFixed(2) + 'deg');    // top edge = +4
        el.style.setProperty('--gx', (x * 100).toFixed(1) + '%');
        el.style.setProperty('--gy', (y * 100).toFixed(1) + '%');
      });
      el.addEventListener('pointerleave', () => {
        el.classList.remove('tilting');
        el.style.setProperty('--rx', '0deg');
        el.style.setProperty('--ry', '0deg');
      });
    });
  }

  // ---- Reveal on scroll (content is visible by default; this only adds the animation) ----

  function reveal() {
    if (REDUCE || !('IntersectionObserver' in window)) return;
    const nodes = $$('.section-head, .feature, .apps > li, .steps > li, .faq-q, .cta > *, .strip');
    if (!nodes.length) return;
    nodes.forEach((n) => {
      n.classList.add('reveal');
      if (n.parentElement && n.parentElement.matches('.apps, .steps, .cta')) {
        const i = Array.prototype.indexOf.call(n.parentElement.children, n);
        n.style.setProperty('--d', Math.min(i, 5) * 80 + 'ms');
      }
    });
    const io = new IntersectionObserver((entries) => {
      entries.forEach((en) => {
        if (!en.isIntersecting) return;
        en.target.classList.add('in');
        io.unobserve(en.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
    nodes.forEach((n) => io.observe(n));
  }

  // ---- Latest release version ----

  const RELEASE_API = 'https://api.github.com/repos/combwizard/folderite/releases/latest';
  const CACHE_KEY = 'folderite-release';
  const CACHE_MS = 10 * 60 * 1000;

  function showVersion(tag) {
    $$('[data-version]').forEach((el) => {
      el.textContent = tag;
      el.hidden = false;
    });
  }

  function latestRelease() {
    if (!$('[data-version]')) return;
    let cached = null;
    try { cached = JSON.parse(sessionStorage.getItem(CACHE_KEY) || 'null'); } catch (e) { /* ignore */ }
    if (cached && cached.tag && Date.now() - (cached.t || 0) < CACHE_MS) {
      showVersion(cached.tag);
      return;
    }
    fetch(RELEASE_API, { headers: { Accept: 'application/vnd.github+json' } })
      .then((r) => { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
      .then((data) => {
        if (!data || typeof data.tag_name !== 'string' || !data.tag_name) return;
        try { sessionStorage.setItem(CACHE_KEY, JSON.stringify({ tag: data.tag_name, t: Date.now() })); } catch (e) { /* ignore */ }
        showVersion(data.tag_name);
      })
      .catch(() => { /* leave the version labels hidden */ });
  }

  // ---- FAQ deep links ----

  function faq() {
    const items = $$('details.faq-q');
    if (!items.length) return;

    function openFromHash() {
      let id = '';
      try { id = decodeURIComponent(location.hash.slice(1)); } catch (e) { return; }
      const el = id && document.getElementById(id);
      if (!el || !el.matches('details.faq-q')) return;
      el.open = true;
      el.scrollIntoView({ block: 'start', behavior: REDUCE ? 'auto' : 'smooth' });
    }
    addEventListener('hashchange', openFromHash);
    openFromHash();

    // Keep the address bar shareable when the user opens a question (no scroll, no history entry).
    items.forEach((q) => {
      q.addEventListener('toggle', () => {
        if (q.open && q.id && location.hash !== '#' + q.id) history.replaceState(null, '', '#' + q.id);
      });
    });
  }

  function boot() {
    scrollEffects();
    heroSpotlight();
    tilt();
    reveal();
    latestRelease();
    faq();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
