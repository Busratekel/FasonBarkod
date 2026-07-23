/**
 * FasonBarkod — QZ Tray (Halil modeli)
 * - Sertifika: window.FASON_QZ_CERT (sunucudan gömülü) veya /api/qz/certificate
 * - İmza: GET /api/qz/sign?request=... (private-key.pem sunucuda)
 */
(function (window) {
    'use strict';

    var STORAGE_KEY = 'fasonbarkod.qz.printer';
    var DEFAULT_KEY = 'fasonbarkod.qz.useDefault';
    var securityConfigured = false;

    function getEmbeddedCert() {
        return window.FASON_QZ_CERT || '';
    }

    function loadCertificate() {
        var embedded = getEmbeddedCert();
        if (embedded) {
            return Promise.resolve(embedded);
        }
        return fetch('/api/qz/certificate', { cache: 'no-store' })
            .then(function (res) {
                if (!res.ok) {
                    throw new Error('QZ sertifikası yapılandırılmamış.');
                }
                return res.text();
            });
    }

    function setupSecurity() {
        if (securityConfigured || typeof qz === 'undefined') {
            return Promise.resolve();
        }

        return loadCertificate().then(function (cert) {
            qz.security.setCertificatePromise(function (resolve, reject) {
                resolve(cert);
            });

            qz.security.setSignatureAlgorithm('SHA512');

            qz.security.setSignaturePromise(function (toSign) {
                return function (resolve, reject) {
                    fetch('/api/qz/sign?request=' + encodeURIComponent(toSign), {
                        cache: 'no-store',
                        credentials: 'same-origin'
                    })
                        .then(function (res) {
                            if (!res.ok) {
                                return res.text().then(function (t) {
                                    throw new Error(t || 'İmza alınamadı');
                                });
                            }
                            return res.text();
                        })
                        .then(resolve)
                        .catch(reject);
                };
            });

            securityConfigured = true;
        });
    }

    function ensureConnected() {
        if (typeof qz === 'undefined') {
            return Promise.reject(new Error('QZ Tray JS yüklenmedi.'));
        }
        return setupSecurity().then(function () {
            if (qz.websocket.isActive()) {
                return;
            }
            return qz.websocket.connect();
        });
    }

    function getSavedPrinter() {
        try { return localStorage.getItem(STORAGE_KEY) || ''; } catch (e) { return ''; }
    }

    function getUseDefault() {
        try { return localStorage.getItem(DEFAULT_KEY) === '1'; } catch (e) { return false; }
    }

    function savePrinter(name, useDefault) {
        try {
            if (useDefault) {
                localStorage.setItem(DEFAULT_KEY, '1');
                localStorage.removeItem(STORAGE_KEY);
            } else {
                localStorage.setItem(DEFAULT_KEY, '0');
                localStorage.setItem(STORAGE_KEY, name || '');
            }
        } catch (e) { /* ignore */ }
    }

    function listPrinters() {
        return ensureConnected().then(function () {
            return qz.printers.find();
        });
    }

    function resolvePrinterName(preferred) {
        if (preferred) return Promise.resolve(preferred);
        var saved = getSavedPrinter();
        if (saved && !getUseDefault()) return Promise.resolve(saved);
        return ensureConnected().then(function () {
            return qz.printers.getDefault();
        });
    }

    function printRawJobs(jobs, printerName) {
        if (!jobs || !jobs.length) {
            return Promise.reject(new Error('Yazdırılacak etiket yok.'));
        }

        return resolvePrinterName(printerName).then(function (printer) {
            if (!printer) {
                throw new Error('Yazıcı seçilmedi.');
            }

            var config = qz.configs.create(printer);
            var data = jobs.map(function (job) {
                return {
                    type: 'raw',
                    format: 'command',
                    flavor: 'plain',
                    data: job
                };
            });

            return ensureConnected().then(function () {
                return qz.print(config, data);
            }).then(function () {
                return { printer: printer, count: jobs.length };
            });
        });
    }

    function printBase64Jobs(base64Jobs, printerName) {
        var jobs = (base64Jobs || []).map(function (b64) {
            var binary = atob(b64);
            var bytes = new Uint8Array(binary.length);
            for (var i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            return new TextDecoder('utf-8').decode(bytes);
        });
        return printRawJobs(jobs, printerName);
    }

    window.FasonQz = {
        ensureConnected: ensureConnected,
        listPrinters: listPrinters,
        getSavedPrinter: getSavedPrinter,
        getUseDefault: getUseDefault,
        savePrinter: savePrinter,
        printRawJobs: printRawJobs,
        printBase64Jobs: printBase64Jobs
    };
})(window);
