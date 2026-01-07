// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Simple chatbot widget logic for BEMART
document.addEventListener('DOMContentLoaded', function () {
    const root = document.getElementById('chatbot-root');
    if (!root) return; // layout cũ không có chatbot

    const panel = document.getElementById('chatbot-panel');
    const toggleBtn = document.getElementById('chatbot-toggle');
    const closeBtn = document.getElementById('chatbot-close');
    const clearBtn = document.getElementById('chatbot-clear');
    const input = document.getElementById('chatbot-input');
    const sendBtn = document.getElementById('chatbot-send');
    const messages = document.getElementById('chatbot-messages');
    const status = document.getElementById('chatbot-status');

    // ====== STATE PERSISTENCE (giữ nguyên log giữa các màn hình) - Tách riêng theo User ID ======
    // Lấy userId từ window.currentUserId (được set từ _Layout.cshtml)
    const userId = window.currentUserId || 'anonymous';
    const STORAGE_KEY = `bemart_chat_log_v1_user_${userId}`;
    const OPEN_KEY = `bemart_chat_open_v1_user_${userId}`;

    /** @type {{ role: 'user' | 'bot', text: string }[]} */
    let history = [];
    let isHistoryLoaded = false;

    // Load history từ database và localStorage
    async function loadHistory() {
        if (isHistoryLoaded) return;
        
        let shouldRender = false;
        let localHistory = [];
        
        // First, try to load from localStorage (fast)
        // localStorage có thể chứa report data
        try {
            const raw = window.localStorage.getItem(STORAGE_KEY);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (Array.isArray(parsed) && parsed.length > 0) {
                    localHistory = parsed;
                    history = parsed;
                    shouldRender = true;
                }
            }
        } catch { /* ignore */ }

        // Then, load from database and update if needed
        if (userId !== 'anonymous' && userId !== 'null') {
            try {
                const res = await fetch('/api/chat/history?limit=100');
                if (res.ok) {
                    const dbHistory = await res.json();
                    if (Array.isArray(dbHistory) && dbHistory.length > 0) {
                        // Merge report data từ localStorage vào history từ database
                        const dbHistoryWithReports = dbHistory.map((msg) => {
                            // Tìm report data từ localStorage history theo text match
                            let reportData = null;
                            const localMsg = localHistory.find(h => 
                                h.role === msg.role && 
                                h.text === msg.message && 
                                h.report
                            );
                            if (localMsg) {
                                reportData = localMsg.report;
                            }
                            
                            return {
                                role: msg.role,
                                text: msg.message,
                                report: reportData
                            };
                        });
                        
                        history = dbHistoryWithReports;
                        
                        // Save to localStorage for faster next load (bao gồm report data)
                        persistHistory();
                        shouldRender = true;
                    }
                }
            } catch (e) {
                console.error('Error loading chat history from database:', e);
                // If database fails, still render from localStorage if available
            }
        }
        
        // Render once after loading
        if (shouldRender) {
            renderHistory();
        }
        
        isHistoryLoaded = true;
    }

    function renderHistory() {
        if (!messages) return;
        messages.innerHTML = '';
        for (const msg of history) {
            if (!msg || (msg.role !== 'user' && msg.role !== 'bot')) continue;
            appendMessage(msg.role, msg.text || '', { 
                skipPersist: true
            });
            // Render report nếu có
            if (msg.report) {
                displayReport(msg.report, { skipPersist: true });
            }
        }
    }

    function persistHistory() {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(history));
        } catch { /* ignore quota errors */ }
    }

    async function clearHistory() {
        // Clear from database
        if (userId !== 'anonymous' && userId !== 'null') {
            try {
                await fetch('/api/chat/clear', {
                    method: 'DELETE'
                });
            } catch (e) {
                console.error('Error clearing chat history from database:', e);
            }
        }
        
        // Clear from localStorage
        history = [];
        try {
            window.localStorage.removeItem(STORAGE_KEY);
        } catch { /* ignore */ }
        
        if (messages) {
            messages.innerHTML = '';
        }
    }

    function setOpenState(isOpen) {
        try {
            window.localStorage.setItem(OPEN_KEY, isOpen ? '1' : '0');
        } catch { /* ignore */ }
    }

    function openPanel() {
        panel.classList.remove('hidden');
        setOpenState(true);
        input.focus();
    }

    function closePanel() {
        panel.classList.add('hidden');
        setOpenState(false);
    }

    toggleBtn?.addEventListener('click', () => {
        if (panel.classList.contains('hidden')) openPanel(); else closePanel();
    });

    closeBtn?.addEventListener('click', closePanel);

    // Nút xoá lịch sử chat
    clearBtn?.addEventListener('click', async function () {
        if (confirm('Bạn có chắc muốn xoá toàn bộ lịch sử trò chuyện không?')) {
            await clearHistory();
        }
    });

    function appendMessage(role, text, options) {
        const wrapper = document.createElement('div');
        // Style như Messenger: user messages align về phải, bot messages align về trái
        if (role === 'user') {
            wrapper.className = 'flex items-start gap-2 mb-1 justify-end';
        } else {
            wrapper.className = 'flex items-start gap-2 mb-1 justify-start w-full';
        }

        const avatar = document.createElement('div');
        avatar.className = 'w-7 h-7 rounded-full flex items-center justify-center text-xs flex-shrink-0';
        if (role === 'user') {
            avatar.classList.add('bg-slate-200', 'text-slate-700');
            avatar.textContent = 'Bạn';
        } else {
            avatar.style.backgroundColor = '#0084FF';
            avatar.classList.add('text-white');
            avatar.innerHTML = '<i class="bi bi-robot"></i>';
        }

        const bubble = document.createElement('div');
        // Bot messages dàn đều theo chiều rộng, user messages giữ max-width
        if (role === 'user') {
            bubble.className = 'px-3 py-2 text-xs whitespace-pre-wrap max-w-[450px]';
        } else {
            bubble.className = 'px-3 py-2 text-xs whitespace-pre-wrap flex-1';
        }
        
        if (role === 'user') {
            // User messages: màu xanh Messenger, text trắng, bo góc như Messenger
            bubble.style.backgroundColor = '#0084FF';
            bubble.style.color = '#FFFFFF';
            bubble.style.borderRadius = '18px 18px 4px 18px'; // Bo góc như Messenger
            bubble.style.boxShadow = '0 1px 2px rgba(0, 0, 0, 0.1)';
        } else {
            // Bot messages: màu xám Messenger, text đen, bo góc như Messenger
            bubble.style.backgroundColor = '#E4E6EB';
            bubble.style.color = '#050505';
            bubble.style.borderRadius = '18px 18px 18px 4px'; // Bo góc như Messenger
            bubble.style.boxShadow = '0 1px 2px rgba(0, 0, 0, 0.05)';
        }
        
        // Hỗ trợ emoji trong text
        bubble.innerHTML = text.replace(/\n/g, '<br>');

        // Thứ tự avatar và bubble tùy thuộc vào role
        if (role === 'user') {
            wrapper.appendChild(bubble);
            wrapper.appendChild(avatar);
        } else {
        wrapper.appendChild(avatar);
        wrapper.appendChild(bubble);
        }
        
        messages.appendChild(wrapper);
        messages.scrollTop = messages.scrollHeight;

        // Lưu vào history trừ khi được yêu cầu bỏ qua (ví dụ khi replay)
        if (!options || !options.skipPersist) {
            history.push({ role, text });
            persistHistory();
        }
    }

    // Load history từ database khi khởi động
    loadHistory();

    // Khôi phục trạng thái mở/đóng panel
    try {
        const openState = window.localStorage.getItem(OPEN_KEY);
        if (openState === '1') {
            panel.classList.remove('hidden');
        }
    } catch { /* ignore */ }

    async function sendMessage() {
        const text = (input.value || '').trim();
        if (!text) return;

        appendMessage('user', text);
        input.value = '';
        status.textContent = 'Đang hỏi AI...';
        sendBtn.disabled = true;

        try {
            const res = await fetch('/api/chat/ask', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ message: text })
            });

            if (!res.ok) {
                appendMessage('bot', 'Không gọi được API chatbot.');
                return;
            }

            const data = await res.json();
            appendMessage('bot', data.reply || 'AI không trả lời.');
            
            // Hiển thị báo cáo nếu có và lưu vào history
            if (data.report) {
                // Lưu report vào entry bot message cuối cùng trong history
                if (history.length > 0 && history[history.length - 1].role === 'bot') {
                    history[history.length - 1].report = data.report;
                    persistHistory();
                }
                displayReport(data.report);
            }
        } catch (e) {
            appendMessage('bot', 'Có lỗi mạng khi gọi chatbot.');
        } finally {
            sendBtn.disabled = false;
            status.textContent = '';
        }
    }

    function displayReport(report, options) {
        if (!report || !messages) return;
        const skipPersist = options && options.skipPersist;

        const reportWrapper = document.createElement('div');
        reportWrapper.className = 'flex items-start gap-2 mb-1 w-full';

        const avatar = document.createElement('div');
        avatar.className = 'w-7 h-7 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center text-xs flex-shrink-0';
        avatar.innerHTML = '<i class="bi bi-file-earmark-text"></i>';

        const reportCard = document.createElement('div');
        reportCard.className = 'px-3 py-2 rounded-2xl border bg-blue-50 border-blue-200 shadow-sm flex-1 text-xs';

        let reportHtml = `<div class="font-semibold text-blue-900 mb-1">${report.title || report.Title || 'Báo cáo'}</div>`;
        
        const summary = report.summary || report.Summary;
        if (summary) {
            reportHtml += `<div class="text-blue-700 mb-2 text-[10px]">${summary}</div>`;
        }

        const rows = report.rows || report.Rows || [];
        if (rows.length > 0) {
            reportHtml += `<div class="text-[10px] text-blue-600 mb-1">${rows.length} dòng dữ liệu</div>`;
        }

        // Tạo button và lưu report data trực tiếp vào closure để tránh bị ghi đè
        reportHtml += `<button class="download-report-btn mt-2 text-[10px] px-2 py-1 bg-blue-600 text-white rounded hover:bg-blue-700">Tải báo cáo</button>`;

        reportCard.innerHTML = reportHtml;

        // Thêm event listener cho button download với report data trong closure
        const downloadBtn = reportCard.querySelector('.download-report-btn');
        if (downloadBtn) {
            // Lưu report data vào closure để mỗi button có report data riêng
            downloadBtn.addEventListener('click', (function(reportData) {
                return function() {
                    downloadReport(reportData);
                };
            })(report));
        }

        reportWrapper.appendChild(avatar);
        reportWrapper.appendChild(reportCard);
        messages.appendChild(reportWrapper);
        messages.scrollTop = messages.scrollHeight;
    }

    function downloadReport(report) {
        if (!report) return;
        
        const csv = convertReportToCSV(report);
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement('a');
        const url = URL.createObjectURL(blob);
        link.setAttribute('href', url);
        link.setAttribute('download', `${report.title || 'report'}_${new Date().toISOString().split('T')[0]}.csv`);
        link.style.visibility = 'hidden';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        // Clean up object URL
        setTimeout(() => URL.revokeObjectURL(url), 100);
    }

    function convertReportToCSV(report) {
        // Support both 'rows' and 'Rows' (C# uses PascalCase, JS might use camelCase)
        const rows = report.rows || report.Rows || [];
        if (!rows || rows.length === 0) return '';

        const headers = Object.keys(rows[0]);
        const csvRows = [headers.join(',')];

        for (const row of rows) {
            const values = headers.map(header => {
                const value = row[header];
                if (value === null || value === undefined) return '';
                // Convert to string and handle commas/quotes
                const strValue = String(value);
                return strValue.includes(',') || strValue.includes('"') || strValue.includes('\n')
                    ? `"${strValue.replace(/"/g, '""')}"` 
                    : strValue;
            });
            csvRows.push(values.join(','));
        }

        // Thêm BOM UTF-8 để Excel hiển thị tiếng Việt đúng
        return '\uFEFF' + csvRows.join('\n');
    }

    sendBtn?.addEventListener('click', sendMessage);

    input?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    // ====== EMOJI FUNCTIONALITY ======
    const emojiStickerBtn = document.getElementById('chatbot-emoji-sticker-btn');
    const picker = document.getElementById('chatbot-emoji-sticker-picker');
    const pickerCloseBtn = document.getElementById('chatbot-picker-close');
    const emojiGrid = document.getElementById('chatbot-emoji-grid');

    // Bộ emoji phổ biến
    const commonEmojis = [
        '😀', '😃', '😄', '😁', '😅', '😂', '🤣', '😊', '😇', '🙂',
        '🙃', '😉', '😌', '😍', '🥰', '😘', '😗', '😙', '😚', '😋',
        '😛', '😝', '😜', '🤪', '🤨', '🧐', '🤓', '😎', '🤩', '🥳',
        '😏', '😒', '😞', '😔', '😟', '😕', '🙁', '☹️', '😣', '😖',
        '😫', '😩', '🥺', '😢', '😭', '😤', '😠', '😡', '🤬', '🤯',
        '😳', '🥵', '🥶', '😱', '😨', '😰', '😥', '😓', '🤗', '🤔',
        '🤭', '🤫', '🤥', '😶', '😐', '😑', '😬', '🙄', '😯', '😦',
        '😧', '😮', '😲', '🥱', '😴', '🤤', '😪', '😵', '🤐', '🥴',
        '🤢', '🤮', '🤧', '😷', '🤒', '🤕', '🤑', '🤠', '😈', '👿',
        '👹', '👺', '🤡', '💩', '👻', '💀', '☠️', '👽', '👾', '🤖',
        '👍', '👎', '👊', '✊', '🤛', '🤜', '🤞', '✌️', '🤟', '🤘',
        '👌', '🤌', '🤏', '👈', '👉', '👆', '🖕', '👇', '☝️', '👋',
        '🤚', '🖐', '✋', '🖖', '👏', '🙌', '🤲', '🤝', '🙏', '✍️',
        '💅', '🤳', '💪', '🦾', '🦿', '🦵', '🦶', '👂', '🦻', '👃',
        '❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '🤎', '💔',
        '❣️', '💕', '💞', '💓', '💗', '💖', '💘', '💝', '💟', '☮️',
        '✝️', '☪️', '🕉', '☸️', '✡️', '🔯', '🕎', '☯️', '☦️', '🛐'
    ];

    // Cache để tránh render lại
    let emojiGridCached = null;

    // Render emoji grid với DocumentFragment và caching
    function renderEmojiGrid() {
        if (!emojiGrid) return;
        
        // Sử dụng cache nếu đã render trước đó
        if (emojiGridCached && emojiGrid.children.length > 0) {
            return;
        }
        
        // Sử dụng DocumentFragment để batch DOM operations
        const fragment = document.createDocumentFragment();
        
        commonEmojis.forEach(emoji => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'text-lg p-2 hover:bg-gray-100 rounded transition-colors';
            btn.textContent = emoji;
            btn.title = emoji;
            btn.dataset.emoji = emoji; // Để event delegation
            fragment.appendChild(btn);
        });
        
        emojiGrid.innerHTML = '';
        emojiGrid.appendChild(fragment);
        emojiGridCached = true;
    }


    // Chèn emoji vào input
    function insertEmoji(emoji) {
        if (!input) return;
        const cursorPos = input.selectionStart || 0;
        const textBefore = input.value.substring(0, cursorPos);
        const textAfter = input.value.substring(cursorPos);
        input.value = textBefore + emoji + textAfter;
        input.focus();
        input.setSelectionRange(cursorPos + emoji.length, cursorPos + emoji.length);
    }


    // Toggle picker
    function togglePicker() {
        if (!picker) return;
        const isHidden = picker.classList.contains('hidden');
        
        if (isHidden) {
            picker.classList.remove('hidden');
            // Render emoji grid nếu chưa có cache
            if (!emojiGridCached || !emojiGrid || emojiGrid.children.length === 0) {
                renderEmojiGrid();
            }
        } else {
            picker.classList.add('hidden');
        }
    }

    function hidePicker() {
        if (picker) picker.classList.add('hidden');
    }

    // Event listeners
    emojiStickerBtn?.addEventListener('click', function(e) {
        e.stopPropagation();
        togglePicker();
    });

    pickerCloseBtn?.addEventListener('click', hidePicker);

    // Event delegation cho emoji buttons
    emojiGrid?.addEventListener('click', function(e) {
        const btn = e.target.closest('button[data-emoji]');
        if (btn) {
            const emoji = btn.dataset.emoji;
            insertEmoji(emoji);
        }
    });

    // Đóng picker khi click bên ngoài
    document.addEventListener('click', function(e) {
        if (picker && emojiStickerBtn && !picker.contains(e.target) && !emojiStickerBtn.contains(e.target)) {
            hidePicker();
        }
    });

});
