(function () {
  const nodes = Array.from(document.querySelectorAll('.node'));
  const edges = Array.from(document.querySelectorAll('.edge'));
  const q = document.getElementById('q');
  const view = document.getElementById('view');
  let variant = 'all';
  let traceMode = false;
  let selectedNodeId = null;

  function reset() {
    selectedNodeId = null;
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
    document.querySelectorAll('button.var:not(#traceBtn)').forEach((btn) => {
      btn.onclick = () => {
        variant = btn.dataset.v;
        document.querySelectorAll('button.var:not(#traceBtn)').forEach((other) => {
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

  function findConnectedGraph(startId) {
    const keepNodes = new Set([startId]);
    const keepEdges = new Set();
    let frontier = [startId];
    const maxDepth = 15;
    let depth = 0;
    while (frontier.length > 0 && depth < maxDepth) {
      const next = [];
      frontier.forEach((curr) => {
        edges.forEach((edge) => {
          if (edge.classList.contains('off')) {
            return;
          }
          const s = edge.dataset.src;
          const d = edge.dataset.dst;
          if (s === curr && !keepEdges.has(edge)) {
            keepEdges.add(edge);
            if (!keepNodes.has(d)) {
              keepNodes.add(d);
              next.push(d);
            }
          } else if (d === curr && !keepEdges.has(edge)) {
            keepEdges.add(edge);
            if (!keepNodes.has(s)) {
              keepNodes.add(s);
              next.push(s);
            }
          }
        });
      });
      frontier = next;
      depth += 1;
    }
    return { keepNodes, keepEdges };
  }

  function focusNode(id) {
    reset();
    selectedNodeId = id;
    if (traceMode) {
      const { keepNodes, keepEdges } = findConnectedGraph(id);
      edges.forEach((edge) => {
        if (keepEdges.has(edge)) {
          edge.classList.add('hot');
        } else {
          edge.classList.add('dim');
        }
      });
      nodes.forEach((node) => {
        if (!keepNodes.has(node.dataset.id)) {
          node.classList.add('dim');
        }
        if (node.dataset.id === id) {
          node.classList.add('sel');
        }
      });
    } else {
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

    const traceBtn = document.getElementById('traceBtn');
    if (traceBtn) {
      traceBtn.addEventListener('click', () => {
        traceMode = !traceMode;
        traceBtn.classList.toggle('on', traceMode);
        traceBtn.textContent = traceMode ? '跨層連鎖：端到端' : '跨層連鎖追蹤';
        if (selectedNodeId) {
          focusNode(selectedNodeId);
        }
      });
    }
  }

  function initLayerControls() {
    const layerBtns = Array.from(document.querySelectorAll('.layer-jump'));
    if (!layerBtns.length) {
      return;
    }
    layerBtns.forEach((btn) => {
      btn.addEventListener('click', () => {
        const targetLayer = btn.dataset.layer;
        layerBtns.forEach((b) => b.classList.toggle('on', b === btn));
        if (targetLayer === 'all') {
          if (view) {
            view.scrollTo({ top: 0, behavior: 'smooth' });
          }
        } else {
          const targetEl = document.getElementById('sec-' + targetLayer);
          if (targetEl && view) {
            const targetY = parseFloat(targetEl.getAttribute('y')) || 0;
            view.scrollTo({ top: Math.max(0, targetY - 20), behavior: 'smooth' });
          }
        }
      });
    });
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
  initLayerControls();
  initTheme();
  initZoom();
  initInfoBox();
})();
