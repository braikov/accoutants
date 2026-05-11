(function () {
  'use strict';
  if (!window.Chart) return;

  const palette = ['#4f46e5', '#0ea5e9', '#10b981', '#f59e0b', '#ef4444', '#a855f7'];
  const vendorLabels = { claude: 'Claude', codex: 'Codex (OpenAI)', gemini: 'Gemini' };

  fetch('/Administration/Charts/DocumentsPerDay?days=30').then(r => r.json()).then(points => {
    new Chart(document.getElementById('chart-documents-per-day'), {
      type: 'line',
      data: {
        labels: points.map(p => p.date.slice(5)),
        datasets: [{
          label: 'Документи',
          data: points.map(p => p.count),
          borderColor: palette[0],
          backgroundColor: palette[0] + '22',
          tension: 0.25,
          fill: true,
          pointRadius: 2,
        }],
      },
      options: {
        responsive: true,
        plugins: { legend: { display: false } },
        scales: { y: { beginAtZero: true, ticks: { precision: 0 } } },
      },
    });
  });

  fetch('/Administration/Charts/VendorDistribution').then(r => r.json()).then(slices => {
    if (slices.length === 0) return;
    new Chart(document.getElementById('chart-vendor-distribution'), {
      type: 'doughnut',
      data: {
        labels: slices.map(s => vendorLabels[s.vendor] || s.vendor),
        datasets: [{
          data: slices.map(s => s.count),
          backgroundColor: slices.map((_, i) => palette[i % palette.length]),
          borderWidth: 0,
        }],
      },
      options: { responsive: true, plugins: { legend: { position: 'bottom' } } },
    });
  });

  fetch('/Administration/Charts/AvgLatency').then(r => r.json()).then(rows => {
    if (rows.length === 0) return;
    new Chart(document.getElementById('chart-avg-latency'), {
      type: 'bar',
      data: {
        labels: rows.map(r => vendorLabels[r.vendor] || r.vendor),
        datasets: [{
          label: 'ms',
          data: rows.map(r => r.avgLatencyMs),
          backgroundColor: rows.map((_, i) => palette[i % palette.length]),
          borderWidth: 0,
        }],
      },
      options: {
        responsive: true,
        indexAxis: 'y',
        plugins: { legend: { display: false } },
        scales: { x: { beginAtZero: true } },
      },
    });
  });
})();
