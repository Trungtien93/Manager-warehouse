(function () {
  'use strict';

  // ---------- DOM refs ----------
  const tbl    = document.getElementById('tbl-details');
  if (!tbl) return;
  const fromWh = document.getElementById('fromWh');
  const btnAdd = document.getElementById('btn-add-row');

  // ---------- Utils ----------
  const toInt = (v) => {
    const n = parseInt((v ?? '').toString().trim(), 10);
    return Number.isNaN(n) ? 0 : n;
  };

  const setOnhandBadge = (badge, val) => {
    // val: number | '—'
    if (val === '—' || val === undefined || val === null || Number.isNaN(val)) {
      badge.textContent = '—';
      badge.classList.remove('text-bg-danger');
      badge.classList.add('text-bg-light');
      return;
    }
    const n = Number(val) || 0;
    badge.textContent = String(n);
    badge.classList.toggle('text-bg-danger', n === 0);
    badge.classList.toggle('text-bg-light', n !== 0);
  };

  const collectMaterialIds = () =>
    Array.from(tbl.querySelectorAll('select.material-select'))
      .map(s => toInt(s.value))
      .filter(x => x > 0)
      .filter((v, i, a) => a.indexOf(v) === i); // unique

  const reindex = () => {
    const rows = Array.from(tbl.querySelectorAll('tbody tr'));
    rows.forEach((tr, i) => {
      tr.querySelectorAll('[name^="Details["]').forEach(el => {
        el.name = el.name.replace(/Details\[[0-9]+\]/, `Details[${i}]`);
        if (el.id) el.id = el.id.replace(/_Details_\d+__/, `_Details_${i}__`);
      });
    });
  };

  // ---------- UI sync ----------
  const syncMaterialOptions = () => {
    const selects = Array.from(tbl.querySelectorAll('select.material-select'));
    const selected = new Set(selects.map(s => s.value).filter(v => v && v !== '0'));
    selects.forEach(sel => {
      const myVal = sel.value;
      Array.from(sel.options).forEach(opt => {
        if (!opt.value || opt.value === '0') { opt.disabled = false; return; }
        opt.disabled = (selected.has(opt.value) && opt.value !== myVal);
      });
    });
  };

  const ensureDefaultQuantities = () => {
    Array.from(tbl.querySelectorAll('.qty-input')).forEach(inp => {
      let v = toInt(inp.value);
      if (v < 1) { v = 1; }
      inp.value = String(v);           // luôn hiển thị số nguyên
    });
  };

  const validateRow = (tr) => {
    const sel      = tr.querySelector('.material-select');
    const qtyInput = tr.querySelector('.qty-input');
    const badge    = tr.querySelector('.onhand-badge');
    const fromId   = toInt(fromWh?.value);

    // Chuẩn hoá qty: nguyên ≥ 1
    let qty = toInt(qtyInput.value);
    if (qty < 1) { qty = 1; qtyInput.value = '1'; }

    const materialSelected = sel && sel.value && sel.value !== '0';

    // Chưa chọn kho hoặc vật tư -> không cảnh báo
    if (!fromId || !materialSelected) {
      qtyInput.classList.remove('is-invalid');
      qtyInput.removeAttribute('title');
      return;
    }

    const onhandRaw = parseFloat(badge.textContent || '0');
    const onhand    = Number.isNaN(onhandRaw) ? 0 : onhandRaw;

    // Cảnh báo khi qty > onhand (kể cả onhand = 0)
    if (qty > onhand) {
      qtyInput.classList.add('is-invalid');
      qtyInput.title = (onhand === 0) ? 'Kho đang hết hàng (tồn = 0).' : `Số lượng vượt tồn (${qty} > ${onhand})`;
    } else {
      qtyInput.classList.remove('is-invalid');
      qtyInput.removeAttribute('title');
    }
  };

  // ---------- Data fetch ----------
  const refreshOnHand = async () => {
    const fromId = toInt(fromWh?.value);
    const ids = collectMaterialIds();

    if (!fromId || ids.length === 0) {
      // Chưa sẵn sàng -> reset badge & xoá cảnh báo
      tbl.querySelectorAll('.onhand-badge').forEach(b => setOnhandBadge(b, '—'));
      tbl.querySelectorAll('.qty-input').forEach(inp => {
        inp.classList.remove('is-invalid');
        inp.removeAttribute('title');
      });
      return;
    }

    try {
      const res = await fetch(`/StockTransfers/OnHand?fromWarehouseId=${fromId}&ids=${ids.join(',')}`);
      const data = await res.json(); // { "10": 123.45, "11": 0, ... }

      Array.from(tbl.querySelectorAll('tbody tr')).forEach(tr => {
        const sel   = tr.querySelector('select.material-select');
        const badge = tr.querySelector('.onhand-badge');
        const qty   = data[sel.value] ?? 0;
        setOnhandBadge(badge, qty);
        validateRow(tr); // re-validate khi tồn đổi
      });
    } catch (e) {
      console.warn('OnHand error', e);
    }
  };

  const refreshUnits = async ({ force = false } = {}) => {
    const ids = collectMaterialIds();
    if (ids.length === 0) return;
    try {
      const res = await fetch(`/StockTransfers/MaterialUnits?ids=${ids.join(',')}`);
      const data = await res.json(); // { "10": "PCS", "11": "KG", ... }

      Array.from(tbl.querySelectorAll('tbody tr')).forEach(tr => {
        const sel       = tr.querySelector('select.material-select');
        const unitInput = tr.querySelector('.unit-input');
        if (!unitInput) return;
        const unit = data[sel.value];
        if (unit) {
          if (force || !unitInput.value || unitInput.value.trim() === '') {
            unitInput.value = unit;
          }
        }
      });
    } catch (e) {
      console.warn('MaterialUnits error', e);
    }
  };

  const fillUnitForRow = async (tr) => {
    const sel       = tr.querySelector('select.material-select');
    const unitInput = tr.querySelector('.unit-input');
    if (!sel || !sel.value) return;
    try {
      const res = await fetch(`/StockTransfers/MaterialUnits?ids=${sel.value}`);
      const data = await res.json(); // { "<id>": "UNIT" }
      const unit = data[sel.value];
      if (unitInput && unit) unitInput.value = unit;
    } catch (e) {
      console.warn('MaterialUnits row error', e);
    }
  };

  // ---------- Events ----------
  btnAdd?.addEventListener('click', () => {
    const first = tbl.querySelector('tbody tr');
    const clone = first.cloneNode(true);

    // reset dòng mới
    const sel = clone.querySelector('.material-select');
    sel.selectedIndex = 0;                           // về optionLabel
    clone.querySelector('.qty-input').value = '1';   // mặc định 1 (nguyên)
    clone.querySelector('.unit-input').value = '';
    setOnhandBadge(clone.querySelector('.onhand-badge'), '—');

    tbl.querySelector('tbody').appendChild(clone);
    reindex();
    syncMaterialOptions();
  });

  // Xoá dòng
  tbl.addEventListener('click', (e) => {
    if (e.target.classList.contains('btn-del-row')) {
      const rows = tbl.querySelectorAll('tbody tr');
      if (rows.length > 1) {
        e.target.closest('tr').remove();
        reindex();
        syncMaterialOptions();
        refreshOnHand();
      }
    }
  });

  // Thay vật tư / sửa số lượng
  tbl.addEventListener('change', (e) => {
    if (e.target.classList.contains('material-select')) {
      syncMaterialOptions();
      fillUnitForRow(e.target.closest('tr')); // fill ĐVT dòng vừa đổi
      refreshOnHand();
    }
  });
  tbl.addEventListener('input', (e) => {
    if (e.target.classList.contains('qty-input')) {
      validateRow(e.target.closest('tr'));
    }
  });

  // Đổi kho đi
  fromWh?.addEventListener('change', refreshOnHand);

  // ---------- Init ----------
  ensureDefaultQuantities();      // luôn ≥ 1 và hiển thị số nguyên
  syncMaterialOptions();          // chống trùng ngay từ đầu
  refreshUnits({ force: false }); // fill ĐVT nếu đang trống
  refreshOnHand();                // ánh xạ tồn theo kho + validate
})();
