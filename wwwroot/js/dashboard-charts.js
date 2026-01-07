// BEMART Dashboard Charts
// Uses Chart.js for data visualization

document.addEventListener('DOMContentLoaded', function() {
    // Initialize all charts
    if (typeof Chart !== 'undefined') {
        initTrendsChart();
        initDistributionChart();
    } else {
        console.error('Chart.js not loaded');
    }
});

// Line Chart: 30-day Trends
async function initTrendsChart() {
    const canvas = document.getElementById('trendsChart');
    if (!canvas) return;

    try {
        const response = await fetch('/Home/GetTrendsChartData');
        const data = await response.json();

        new Chart(canvas, {
            type: 'line',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        labels: {
                            usePointStyle: true,
                            padding: 15,
                            font: {
                                size: 12,
                                weight: '600',
                                family: "'Inter', sans-serif"
                            },
                            color: '#475569'
                        }
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        backgroundColor: 'rgba(255, 255, 255, 0.95)',
                        titleColor: '#0f172a',
                        bodyColor: '#475569',
                        borderColor: '#e2e8f0',
                        borderWidth: 1,
                        padding: 12,
                        displayColors: true,
                        boxPadding: 6,
                        cornerRadius: 8,
                        titleFont: {
                            size: 13,
                            weight: '700',
                            family: "'Inter', sans-serif"
                        },
                        bodyFont: {
                            size: 12,
                            weight: '600',
                            family: "'Inter', sans-serif"
                        }
                    }
                },
                scales: {
                    x: {
                        grid: {
                            display: false,
                            drawBorder: false
                        },
                        ticks: {
                            font: {
                                size: 11,
                                weight: '500',
                                family: "'Inter', sans-serif"
                            },
                            color: '#64748b'
                        }
                    },
                    y: {
                        beginAtZero: true,
                        grid: {
                            color: '#f1f5f9',
                            drawBorder: false
                        },
                        ticks: {
                            font: {
                                size: 11,
                                weight: '500',
                                family: "'Inter', sans-serif"
                            },
                            color: '#64748b',
                            callback: function(value) {
                                return value.toLocaleString('vi-VN');
                            }
                        }
                    }
                },
                interaction: {
                    mode: 'nearest',
                    axis: 'x',
                    intersect: false
                },
                elements: {
                    point: {
                        radius: 4,
                        hoverRadius: 6,
                        borderWidth: 2
                    },
                    line: {
                        borderWidth: 3,
                        tension: 0.4
                    }
                }
            }
        });
    } catch (error) {
        console.error('Error loading trends chart:', error);
        canvas.parentElement.innerHTML = '<p class="text-center text-slate-500">Không thể tải biểu đồ</p>';
    }
}

// Pie Chart: Distribution by Warehouse
async function initDistributionChart() {
    const canvas = document.getElementById('distributionChart');
    if (!canvas) return;

    try {
        const response = await fetch('/Home/GetDistributionChartData');
        const data = await response.json();

        new Chart(canvas, {
            type: 'doughnut',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '65%',
                plugins: {
                    legend: {
                        display: true,
                        position: 'right',
                        labels: {
                            usePointStyle: true,
                            padding: 12,
                            font: {
                                size: 12,
                                weight: '600',
                                family: "'Inter', sans-serif"
                            },
                            color: '#475569'
                        }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(255, 255, 255, 0.95)',
                        titleColor: '#0f172a',
                        bodyColor: '#475569',
                        borderColor: '#e2e8f0',
                        borderWidth: 1,
                        padding: 12,
                        displayColors: true,
                        boxPadding: 6,
                        cornerRadius: 8,
                        titleFont: {
                            size: 13,
                            weight: '700',
                            family: "'Inter', sans-serif"
                        },
                        bodyFont: {
                            size: 12,
                            weight: '600',
                            family: "'Inter', sans-serif"
                        },
                        callbacks: {
                            label: function(context) {
                                const label = context.label || '';
                                const value = context.parsed || 0;
                                const total = context.dataset.data.reduce((a, b) => a + b, 0);
                                const percentage = ((value / total) * 100).toFixed(1);
                                return `${label}: ${value.toLocaleString('vi-VN')} (${percentage}%)`;
                            }
                        }
                    }
                },
                elements: {
                    arc: {
                        borderWidth: 0
                    }
                }
            }
        });
    } catch (error) {
        console.error('Error loading distribution chart:', error);
        canvas.parentElement.innerHTML = '<p class="text-center text-slate-500">Không thể tải biểu đồ</p>';
    }
}



