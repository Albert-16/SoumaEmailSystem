// =============================================================================
// SoumaCharts — Módulo de interop con Chart.js para el dashboard de trazabilidad
// Utilizado por ChartInteropService.cs vía IJSRuntime
// =============================================================================
window.SoumaCharts = {
    _instances: {},

    // Destruye un gráfico existente si hay uno en el canvas
    destroyChart: function (canvasId) {
        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
            delete this._instances[canvasId];
        }
    },

    // Gráfico de línea: volumen de emails por día (últimos 7 días)
    renderLineChart: function (canvasId, labels, data) {
        this.destroyChart(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        this._instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Emails enviados',
                    data: data,
                    borderColor: '#3b82f6',
                    backgroundColor: 'rgba(59, 130, 246, 0.1)',
                    borderWidth: 2,
                    fill: true,
                    tension: 0.4,
                    pointBackgroundColor: '#3b82f6',
                    pointBorderColor: '#fff',
                    pointBorderWidth: 2,
                    pointRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: '#1a1d27',
                        titleColor: '#fff',
                        bodyColor: '#ccc',
                        borderColor: '#333',
                        borderWidth: 1
                    }
                },
                scales: {
                    x: {
                        grid: { color: 'rgba(255,255,255,0.05)' },
                        ticks: { color: '#888' }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: 'rgba(255,255,255,0.05)' },
                        ticks: { color: '#888' }
                    }
                }
            }
        });
    },

    // Gráfico de dona: distribución de estados (Sent/Failed/Pending/Retrying)
    renderDoughnutChart: function (canvasId, labels, data, colors) {
        this.destroyChart(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        this._instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors,
                    borderColor: '#1a1d27',
                    borderWidth: 3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '65%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { color: '#ccc', padding: 12, usePointStyle: true }
                    },
                    tooltip: {
                        backgroundColor: '#1a1d27',
                        titleColor: '#fff',
                        bodyColor: '#ccc'
                    }
                }
            }
        });
    },

    // Gráfico de barras: emails por microservicio
    renderBarChart: function (canvasId, labels, data) {
        this.destroyChart(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        this._instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Emails',
                    data: data,
                    backgroundColor: 'rgba(59, 130, 246, 0.7)',
                    borderColor: '#3b82f6',
                    borderWidth: 1,
                    borderRadius: 6
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: '#1a1d27',
                        titleColor: '#fff',
                        bodyColor: '#ccc'
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: { color: '#888' }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: 'rgba(255,255,255,0.05)' },
                        ticks: { color: '#888' }
                    }
                }
            }
        });
    },

    // Descarga un archivo desde bytes (usado para CSV export)
    downloadFile: function (fileName, contentType, base64Content) {
        const link = document.createElement('a');
        link.download = fileName;
        link.href = `data:${contentType};base64,${base64Content}`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    }
};
