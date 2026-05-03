/**
 * Cena Question Figure Samples — rendering script
 *
 * Renders function plots (via function-plot.js) and physics diagrams
 * (programmatic SVG) for the sample question cards.
 *
 * This is a standalone demo. In production these will be Vue components
 * (<QuestionFigure>) driven by a FigureSpec payload from the API.
 */

/* ── Theme toggle ────────────────────────────────────────────── */
function toggleTheme() {
  document.body.classList.toggle('dark');
  requestAnimationFrame(renderPlots);
}

/* ── Physics Diagram: Inclined Plane (programmatic SVG) ──────── */
function buildInclinedPlaneSVG() {
  const isDark = document.body.classList.contains('dark');
  const textColor  = isDark ? '#CFD3EC' : '#4B465C';
  const lineColor  = isDark ? '#8692D0' : '#4B465C';
  const surfColor  = isDark ? '#434968' : '#DBDADE';

  const W = 600, H = 380;
  const ox = 80, oy = H - 60;
  const baseLen = 420;
  const theta = 30;
  const rad = theta * Math.PI / 180;
  const height = baseLen * Math.tan(rad);
  const tx = ox + baseLen;
  const ty = oy - height;

  // Block on incline (42% up)
  const frac = 0.42;
  const bx = ox + baseLen * frac;
  const by = oy - height * frac;
  const bw = 50, bh = 40;

  const cosA = Math.cos(-rad), sinA = Math.sin(-rad);
  function rot(dx, dy) {
    return [bx + dx * cosA - dy * sinA, by + dx * sinA + dy * cosA];
  }
  const corners = [rot(-bw/2, 0), rot(bw/2, 0), rot(bw/2, -bh), rot(-bw/2, -bh)];
  const blockPath = corners.map(function(p, i) {
    return (i === 0 ? 'M' : 'L') + p[0].toFixed(1) + ',' + p[1].toFixed(1);
  }).join(' ') + ' Z';

  // Force arrow center
  const cx = bx;
  const cy = by - (bh / 2) * Math.cos(rad);

  // Arrow builder (returns an SVG fragment string)
  function arrow(x1, y1, x2, y2, color, label, ldx, ldy, dashed) {
    var id = 'ah' + Math.random().toString(36).slice(2, 8);
    var dashAttr = dashed ? ' stroke-dasharray="6 4"' : '';
    return '<defs><marker id="' + id + '" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto">' +
      '<polygon points="0 0, 8 3, 0 6" fill="' + color + '"/></marker></defs>' +
      '<line x1="' + x1.toFixed(1) + '" y1="' + y1.toFixed(1) +
      '" x2="' + x2.toFixed(1) + '" y2="' + y2.toFixed(1) +
      '" stroke="' + color + '" stroke-width="2.5" marker-end="url(#' + id + ')"' + dashAttr + '/>' +
      '<text x="' + (x2 + ldx).toFixed(1) + '" y="' + (y2 + ldy).toFixed(1) +
      '" fill="' + color + '" font-size="14" font-weight="600" font-family="Inter, sans-serif"' +
      ' dominant-baseline="middle">' + label + '</text>';
  }

  var scale = 1.8;
  var mg = 80 * scale;

  // Force endpoints
  var gx2 = cx, gy2 = cy + mg * 0.6;
  var N = mg * Math.cos(rad);
  var nx2 = cx + N * 0.5 * Math.sin(rad) * (-1);
  var ny2 = cy - N * 0.5 * Math.cos(rad);
  var mgSin = mg * Math.sin(rad);
  var msx2 = cx + mgSin * 0.5 * Math.cos(rad);
  var msy2 = cy + mgSin * 0.5 * Math.sin(rad);
  var mgCos = mg * Math.cos(rad);
  var mcx2 = cx + mgCos * 0.4 * Math.sin(rad);
  var mcy2 = cy + mgCos * 0.4 * Math.cos(rad);
  var Ff = 0.2 * mgCos;
  var fx2 = cx - Ff * 0.8 * Math.cos(rad);
  var fy2 = cy - Ff * 0.8 * Math.sin(rad);

  // Angle arc
  var arcR = 50;
  var arcEndX = ox + arcR * Math.cos(rad);
  var arcEndY = oy - arcR * Math.sin(rad);

  // Ground hatching
  var hatches = '';
  for (var i = 0; i < 18; i++) {
    var gx = 50 + i * 30;
    hatches += '<line x1="' + gx + '" y1="' + oy + '" x2="' + (gx - 8) +
      '" y2="' + (oy + 12) + '" stroke="' + surfColor + '" stroke-width="1.5"/>';
  }

  // Surface texture
  var surfTex = '';
  for (var j = 0; j < 10; j++) {
    var f = 0.08 + j * 0.09;
    var hx = ox + baseLen * f;
    var hy = oy - height * f;
    surfTex += '<line x1="' + hx.toFixed(1) + '" y1="' + hy.toFixed(1) +
      '" x2="' + (hx - 6 * Math.cos(rad) - 8 * Math.sin(rad)).toFixed(1) +
      '" y2="' + (hy + 6 * Math.sin(rad) + 8 * Math.cos(rad)).toFixed(1) +
      '" stroke="' + surfColor + '" stroke-width="1.2"/>';
  }

  var svg = '<svg viewBox="0 0 ' + W + ' ' + H + '" xmlns="http://www.w3.org/2000/svg"' +
    ' style="font-family: Inter, sans-serif;">' +
    // Ground
    '<line x1="40" y1="' + oy + '" x2="' + (W - 20) + '" y2="' + oy +
    '" stroke="' + lineColor + '" stroke-width="2"/>' +
    hatches +
    // Incline surface
    '<line x1="' + ox + '" y1="' + oy + '" x2="' + tx + '" y2="' + ty +
    '" stroke="' + lineColor + '" stroke-width="2.5"/>' +
    // Vertical dashed
    '<line x1="' + tx + '" y1="' + ty + '" x2="' + tx + '" y2="' + oy +
    '" stroke="' + lineColor + '" stroke-width="1.5" stroke-dasharray="6 3"/>' +
    surfTex +
    // Angle arc
    '<path d="M ' + (ox + arcR) + ',' + oy + ' A ' + arcR + ',' + arcR +
    ' 0 0,1 ' + arcEndX.toFixed(1) + ',' + arcEndY.toFixed(1) +
    '" fill="none" stroke="' + textColor + '" stroke-width="1.5"/>' +
    '<text x="' + (ox + arcR * 0.7).toFixed(1) + '" y="' + (oy - arcR * 0.22).toFixed(1) +
    '" fill="' + textColor + '" font-size="16" font-style="italic" font-weight="500">\u03B8 = 30\u00B0</text>' +
    // Block
    '<path d="' + blockPath + '" fill="rgba(115, 103, 240, 0.15)" stroke="#7367F0" stroke-width="2"/>' +
    '<text x="' + (bx - 4).toFixed(1) + '" y="' + (by - bh * 0.3).toFixed(1) +
    '" fill="' + textColor + '" font-size="15" font-weight="700" text-anchor="middle" dominant-baseline="middle">m</text>' +
    // Forces
    arrow(cx, cy + 5, gx2, gy2, '#EA5455', 'mg', 8, 5, false) +
    arrow(cx, cy - 5, nx2, ny2, '#28C76F', 'N', -25, -5, false) +
    arrow(cx - 3, cy, fx2, fy2, '#FF9F43', 'F\u2092', -30, -2, false) +
    arrow(cx + 5, cy + 3, msx2, msy2, '#EA5455', 'mg sin \u03B8', 8, 18, true) +
    arrow(cx - 3, cy + 5, mcx2 - 15, mcy2 + 20, '#EA5455', 'mg cos \u03B8', -75, 15, true) +
    // Coordinate axes
    '<g transform="translate(' + (W - 90) + ', 50)">' +
    arrow(0, 0, 45, 0, textColor, 'x', 5, 4, false) +
    arrow(0, 0, 0, -45, textColor, 'y', -5, -8, false) +
    '</g>' +
    // Constraint label
    '<text x="' + (tx - 10) + '" y="' + (ty - 15) +
    '" fill="' + textColor + '" font-size="12" font-style="italic" text-anchor="end">N \u2265 0</text>' +
    '</svg>';

  return svg;
}

function renderPhysicsDiagram() {
  var container = document.getElementById('physics-incline');
  if (!container) return;
  // Using createElementNS for safe SVG injection
  var svgStr = buildInclinedPlaneSVG();
  var parser = new DOMParser();
  var doc = parser.parseFromString(svgStr, 'image/svg+xml');
  var svgEl = doc.documentElement;
  container.replaceChildren(svgEl);
}

/* ── Render function plots via function-plot.js ──────────────── */
function renderPlots() {
  var curveColor = '#7367F0';

  var commonOptions = {
    grid: true,
    tip: { xLine: true, yLine: true }
  };

  // Plot 1: Quadratic
  try {
    var el1 = document.getElementById('plot-quadratic');
    if (el1) el1.replaceChildren();
    functionPlot(Object.assign({}, commonOptions, {
      target: '#plot-quadratic',
      width: 680,
      height: 320,
      xAxis: { domain: [-1, 5], label: 'x' },
      yAxis: { domain: [-2, 6], label: 'f(x)' },
      data: [
        { fn: 'x^2 - 4*x + 3', color: curveColor,
          derivative: { fn: '2*x - 4', updateOnMouseMove: true } },
        { points: [[1, 0], [3, 0]], fnType: 'points', graphType: 'scatter',
          color: '#EA5455', attr: { r: 5 } },
        { points: [[2, -1]], fnType: 'points', graphType: 'scatter',
          color: '#28C76F', attr: { r: 6 } }
      ]
    }));
  } catch (e) { console.warn('Plot 1:', e); }

  // Plot 2: Trig
  try {
    var el2 = document.getElementById('plot-trig');
    if (el2) el2.replaceChildren();
    functionPlot(Object.assign({}, commonOptions, {
      target: '#plot-trig',
      width: 680,
      height: 300,
      xAxis: { domain: [-1, 7], label: 'x' },
      yAxis: { domain: [-3, 3], label: 'f(x)' },
      data: [
        { fn: '2*sin(2*x)', color: curveColor },
        { points: [[0, 0], [Math.PI / 2, 0], [Math.PI, 0],
                   [3 * Math.PI / 2, 0], [2 * Math.PI, 0]],
          fnType: 'points', graphType: 'scatter',
          color: '#EA5455', attr: { r: 4 } }
      ]
    }));
  } catch (e) { console.warn('Plot 2:', e); }

  // Plot 3: Quadratic (Arabic)
  try {
    var el3 = document.getElementById('plot-quadratic-ar');
    if (el3) el3.replaceChildren();
    functionPlot(Object.assign({}, commonOptions, {
      target: '#plot-quadratic-ar',
      width: 680,
      height: 320,
      xAxis: { domain: [-1, 5], label: 'x' },
      yAxis: { domain: [-2, 6], label: 'f(x)' },
      data: [
        { fn: 'x^2 - 4*x + 3', color: curveColor },
        { points: [[1, 0], [3, 0]], fnType: 'points', graphType: 'scatter',
          color: '#EA5455', attr: { r: 5 } },
        { points: [[2, -1]], fnType: 'points', graphType: 'scatter',
          color: '#28C76F', attr: { r: 6 } }
      ]
    }));
  } catch (e) { console.warn('Plot 3:', e); }

  // Plot 4: Derivative with tangent line
  try {
    var el4 = document.getElementById('plot-derivative');
    if (el4) el4.replaceChildren();
    functionPlot(Object.assign({}, commonOptions, {
      target: '#plot-derivative',
      width: 680,
      height: 320,
      xAxis: { domain: [-2.5, 3], label: 'x' },
      yAxis: { domain: [-4, 5], label: 'f(x)' },
      data: [
        { fn: 'x^3 - 3*x + 1', color: curveColor },
        { fn: '-1', color: '#FF9F43', nSamples: 2 },
        { points: [[1, -1]], fnType: 'points', graphType: 'scatter',
          color: '#28C76F', attr: { r: 6 } }
      ]
    }));
  } catch (e) { console.warn('Plot 4:', e); }

  // Physics diagram
  renderPhysicsDiagram();
}

/* ── Choice click handler ────────────────────────────────────── */
document.addEventListener('click', function (e) {
  var choice = e.target.closest('.choice');
  if (!choice) return;
  var group = choice.closest('.choices');
  var all = group.querySelectorAll('.choice');
  for (var i = 0; i < all.length; i++) {
    all[i].classList.remove('selected');
    all[i].setAttribute('aria-checked', 'false');
  }
  choice.classList.add('selected');
  choice.setAttribute('aria-checked', 'true');
});

/* ── Init ────────────────────────────────────────────────────── */
window.addEventListener('DOMContentLoaded', function () {
  setTimeout(renderPlots, 200);
});
