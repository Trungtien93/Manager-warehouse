// BEMART Service Worker for PWA
const CACHE_NAME = 'bemart-cache-v4'; // Tăng version để clear cache cũ (v4: remove all missing icons)
const OFFLINE_URL = '/offline.html';

// Files to cache on install - CHỈ cache static assets, KHÔNG cache HTML pages
const urlsToCache = [
    '/css/toast.css',
    '/js/toast.js',
    '/js/shortcuts.js',
    '/js/site.js',
    '/uploads/avarta.png',
    '/offline.html'
];

// Install event - cache files
self.addEventListener('install', (event) => {
    console.log('[Service Worker] Installing...');
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => {
                console.log('[Service Worker] Caching app shell');
                return cache.addAll(urlsToCache.map(url => new Request(url, {credentials: 'same-origin'})));
            })
            .catch((error) => {
                console.error('[Service Worker] Cache failed:', error);
            })
    );
    self.skipWaiting();
});

// Activate event - clean old caches
self.addEventListener('activate', (event) => {
    console.log('[Service Worker] Activating...');
    event.waitUntil(
        caches.keys().then((cacheNames) => {
            return Promise.all(
                cacheNames.map((cacheName) => {
                    if (cacheName !== CACHE_NAME) {
                        console.log('[Service Worker] Deleting old cache:', cacheName);
                        return caches.delete(cacheName);
                    }
                })
            );
        })
    );
    self.clients.claim();
});

// Fetch event - KHÔNG cache HTML pages, chỉ cache static assets
self.addEventListener('fetch', (event) => {
    // Skip non-GET requests
    if (event.request.method !== 'GET') return;

    // Skip chrome extensions and other protocols
    if (!event.request.url.startsWith('http')) return;

    const url = new URL(event.request.url);
    
    // Kiểm tra nếu là HTML page (không có extension hoặc là root path)
    const isHtmlPage = url.pathname === '/' || 
                      url.pathname.endsWith('.html') ||
                      (!url.pathname.match(/\.(js|css|png|jpg|jpeg|svg|gif|ico|woff|woff2|json)$/) && 
                       !url.pathname.startsWith('/api/') &&
                       !url.pathname.startsWith('/css/') &&
                       !url.pathname.startsWith('/js/') &&
                       !url.pathname.startsWith('/uploads/'));

    // Nếu là HTML page, luôn fetch từ network, KHÔNG cache
    if (isHtmlPage) {
        event.respondWith(
            fetch(event.request)
                .then((response) => {
                    return response;
                })
                .catch(() => {
                    // Offline fallback
                    return caches.match(OFFLINE_URL);
                })
        );
        return;
    }

    // Static assets: serve from cache, fallback to network
    event.respondWith(
        caches.match(event.request)
            .then((response) => {
                // Cache hit - return response
                if (response) {
                    return response;
                }

                // Clone the request
                const fetchRequest = event.request.clone();

                return fetch(fetchRequest).then((response) => {
                    // Check if valid response
                    if (!response || response.status !== 200 || response.type !== 'basic') {
                        return response;
                    }

                    // Clone the response
                    const responseToCache = response.clone();

                    // Chỉ cache static assets (JS, CSS, images, fonts)
                    const url = new URL(event.request.url);
                    if (url.pathname.match(/\.(js|css|png|jpg|jpeg|svg|gif|ico|woff|woff2)$/)) {
                        caches.open(CACHE_NAME)
                            .then((cache) => {
                                cache.put(event.request, responseToCache);
                            });
                    }

                    return response;
                }).catch(() => {
                    // Offline fallback cho static assets
                    return caches.match(OFFLINE_URL);
                });
            })
    );
});

// Background sync (optional - for future enhancement)
self.addEventListener('sync', (event) => {
    console.log('[Service Worker] Sync event:', event.tag);
    if (event.tag === 'sync-data') {
        event.waitUntil(syncData());
    }
});

async function syncData() {
    // Placeholder for syncing data when back online
    console.log('[Service Worker] Syncing data...');
}

// Push notifications (optional - for future enhancement)
self.addEventListener('push', (event) => {
    console.log('[Service Worker] Push received');
    const options = {
        body: event.data ? event.data.text() : 'Thông báo từ BEMART',
        icon: '/images/icons/icon-192x192.png',
        badge: '/images/icons/icon-96x96.png',
        vibrate: [200, 100, 200],
        data: {
            dateOfArrival: Date.now(),
            primaryKey: 1
        }
    };

    event.waitUntil(
        self.registration.showNotification('BEMART', options)
    );
});

// Notification click
self.addEventListener('notificationclick', (event) => {
    console.log('[Service Worker] Notification click');
    event.notification.close();
    
    event.waitUntil(
        clients.openWindow('/')
    );
});


