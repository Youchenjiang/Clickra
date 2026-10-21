(function () {
  const nodes = Array.from(document.querySelectorAll('.node'));
  const edges = Array.from(document.querySelectorAll('.edge'));
  const q = document.getElementById('q');
  const view = document.getElementById('view');
  let variant = 'all';

  function reset() {
    document.querySelectorAll('.dim,.hot,.sel').forEach((el) => {
      el.classList.remove('dim', 'hot', 'sel');
    });
  }

  function applyVariant() {
    edges.forEach((edge) => {
      const v = edge.dataset.variant;
      const isOff = variant !== 'all' && v !== 'both' && v !== variant;
      edge.classList.toggle('off', isOff);
    });
  }

  function initVariantSelector() {
    document.querySelectorAll('button.var').forEach((btn) => {
      btn.onclick = () => {
        variant = btn.dataset.v;
        document.querySelectorAll('button.var').forEach((other) => {
          other.classList.toggle('on', other === btn);
        });
        applyVariant();
        reset();
        if (q) {
          q.value = '';
        }
      };
    });
  }

  function focusNode(id) {
    reset();
    const keep = new Set([id]);
    edges.forEach((edge) => {
      if (edge.classList.contains('off')) {
        return;
      }
      if (edge.dataset.src === id || edge.dataset.dst === id) {
        edge.classList.add('hot');
        keep.add(edge.dataset.src);
        keep.add(edge.dataset.dst);
      } else {
        edge.classList.add('dim');
      }
    });
    nodes.forEach((node) => {
      if (!keep.has(node.dataset.id)) {
        node.classList.add('dim');
      }
      if (node.dataset.id === id) {
        node.classList.add('sel');
      }
    });
  }

  function initNodeEvents() {
    nodes.forEach((node) => {
      node.addEventListener('click', (ev) => {
        ev.stopPropagation();
        if (q) {
          q.value = '';
        }
        focusNode(node.dataset.id);
      });
    });

    if (view) {
      view.addEventListener('click', () => {
        reset();
      });
    }
  }

  function initSearch() {
    if (!q) {
      return;
    }
    q.addEventListener('input', () => {
      reset();
      const s = q.value.trim().toLowerCase();
      if (!s) {
        return;
      }
      nodes.forEach((node) => {
        if (!node.dataset.search?.includes(s)) {
          node.classList.add('dim');
        }
      });
      edges.forEach((edge) => {
        edge.classList.add('dim');
      });
    });
  }

  function initActionButtons() {
    const unBtn = document.getElementById('unBtn');
    if (unBtn) {
      let un = false;
      unBtn.onclick = () => {
        un = !un;
        document.body.classList.toggle('hideUn', un);
        unBtn.textContent = un ? '顯示未發佈（Fluent）' : '隱藏未發佈（Fluent）';
        unBtn.classList.toggle('on', un);
      };
    }

    const resetBtn = document.getElementById('resetBtn');
    if (resetBtn) {
      resetBtn.onclick = () => {
        if (q) {
          q.value = '';
        }
        reset();
      };
    }
  }

  function initTheme() {
    const root = document.documentElement;
    const themeBtn = document.getElementById('themeBtn');
    if (!themeBtn) {
      return;
    }
    let t = null;
    try {
      t = localStorage.getItem('clickra-arch-theme');
    } catch {
      // Ignore localStorage access failures
    }
    if (!t) {
      t = window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    function applyTheme() {
      root.dataset.theme = t;
      themeBtn.textContent = t === 'dark' ? '淺色' : '深色';
    }
    applyTheme();
    themeBtn.onclick = () => {
      t = t === 'dark' ? 'light' : 'dark';
      applyTheme();
      try {
        localStorage.setItem('clickra-arch-theme', t);
      } catch {
        // Ignore localStorage access failures
      }
    };
  }

  function initZoom() {
    if (!view) {
      return;
    }
    const svg = view.querySelector('svg');
    if (!svg) {
      return;
    }
    const base = window.devicePixelRatio || 1;
    function zoom() {
      const z = (window.devicePixelRatio || 1) / base;
      return Math.abs(z - 1) < 0.02 ? 1 : z;
    }
    const hdr = document.querySelector('header');
    function fit() {
      const z = zoom();
      if (hdr) {
        hdr.style.zoom = z === 1 ? '' : String(1 / z);
      }
      if (z === 1) {
        svg.style.width = '';
        svg.style.maxWidth = '';
        view.style.overflowX = 'hidden';
        return;
      }
      view.style.overflowX = 'auto';
      svg.style.maxWidth = 'none';
      svg.style.width = Math.floor(Math.min(1556, view.clientWidth * z)) + 'px';
    }
    fit();
    window.addEventListener('resize', fit);
  }

  function initInfoBox() {
    const infoBtn = document.getElementById('infoBtn');
    const infoBox = document.getElementById('infoBox');
    if (infoBtn && infoBox) {
      infoBtn.onclick = () => {
        infoBox.hidden = !infoBox.hidden;
        infoBtn.classList.toggle('on', !infoBox.hidden);
        window.dispatchEvent(new Event('resize'));
      };
    }
  }

  initVariantSelector();
  initNodeEvents();
  initSearch();
  initActionButtons();
  initTheme();
  initZoom();
  initInfoBox();
})();
