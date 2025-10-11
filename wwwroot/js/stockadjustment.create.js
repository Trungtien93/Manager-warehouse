// wwwroot/js/stockadjustment.create.js
(function(){
  'use strict';

  const tbl   = document.getElementById('tbl-details');
  if (!tbl) return;
  const whSel = document.getElementById('warehouseId');
  const btnAdd = document.getElementById('btn-add-row');

  // ---------- utils ----------
  const toNum = (v) => {
    const n = parseFloat((v ?? '').toString().trim());
    return Number.isNaN(n) ? 0 : n;
  };
  const toInt = (v) => {
    const n = parseInt((v ?? '').toString().trim(), 10);
    return Number.isNaN(n) ? 0 : n;
  };

  const setOnhandBadge = (badge, val) => {
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
      .map(s => toInt(s.value)).filter(x => x > 0)
      .filter((v,i,a) => a.indexOf(v) === i);

  const reindex = () => {
    Array.from(tbl.querySelectorAll('tbody tr')).forEach((tr, i) => {
      tr.querySelectorAll('[name^="Details["]').forEach(el => {
        el.name = el.name.replace(/Details\[[0-9]+\]/, `Details[${i}]`);
        if (el.id) el.id = el.id.replace(/_Details_\d+__/, `_Details_${i}__`);
      });
    });
  };

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

  // ---------- validation ----------
  const validateRow = (tr) => {
    const matSel   = tr.querySelector('.material-select');
    const diffInp  = tr.querySelector('.diff-input');
    const badge    = tr.querySelector('.onhand-badge');
    const whId     = toInt(whSel?.value);

    let diff = toNum(diffInp.value); // có thể âm/dương; 0 cho phép nhưng server sẽ lọc bỏ khi lưu
    // Không cảnh báo khi chưa chọn kho hoặc vật tư
    if (!whId || !(matSel && matSel.value && matSel.value !== '0')) {
      diffInp.classList.remove('is-invalid');
      diffInp.removeAttribute('title');
      return;
    }
    const onhandRaw = parseFloat(badge.textContent || '0');
    const onhand    = Number.isNaN(onhandRaw) ? 0 : onhandRaw;
    const final     = onhand + diff;

    if (final < 0) {
      diffInp.classList.add('is-invalid');
      diffInp.title = `Sau điều chỉnh sẽ âm: ${onhand} + (${diff}) = ${final}`;
    } else {
      diffInp.classList.remove('is-invalid');
      diffInp.removeAttribute('title');
    }
  };

  // ---------- fetchers ----------
  const refreshOnHand = async () => {
    const whId = toInt(whSel?.value);
    const ids  = collectMaterialIds();
    if (!whId || ids.length === 0) {
      tbl.querySelectorAll('.onhand-badge').forEach(b => setOnhandBadge(b, '—'));
      tbl.querySelectorAll('.diff-input').forEach(i => { i.classList.remove('is-invalid'); i.removeAttribute('title'); });
      return;
    }
    try{
      const res = await fetch(`/StockAdjustments/OnHand?warehouseId=${whId}&ids=${ids.join(',')}`);
      const data = await res.json(); // { "10": 12.5, ... }
      Array.from(tbl.querySelectorAll('tbody tr')).forEach(tr => {
        const sel   = tr.querySelector('select.material-select');
        const badge = tr.querySelector('.onhand-badge');
        const qty   = data[sel.value] ?? 0;
        setOnhandBadge(badge, qty);
        validateRow(tr);
      });
    }catch(e){ console.warn('OnHand error', e); }
  };

  const refreshUnits = async ({ force = false } = {}) => {
    const ids = collectMaterialIds();
    if (ids.length === 0) return;
    try{
      const res = await fetch(`/StockAdjustments/MaterialUnits?ids=${ids.join(',')}`);
      const data = await res.json(); // { "10": "PCS", ... }
      Array.from(tbl.querySelectorAll('tbody tr')).forEach(tr => {
        const sel   = tr.querySelector('select.material-select');
        const label = tr.querySelector('.unit-label');
        const unit  = data[sel.value];
        if (label && unit) label.textContent = unit;
        if (force && !unit) label.textContent = '—';
      });
    }catch(e){ console.warn('MaterialUnits error', e); }
  };

  const fillUnitForRow = async (tr) => {
    const sel   = tr.querySelector('select.material-select');
    const label = tr.querySelector('.unit-label');
    if (!sel || !sel.value) { if (label) label.textContent = '—'; return; }
    try{
      const res = await fetch(`/StockAdjustments/MaterialUnits?ids=${sel.value}`);
      const data = await res.json();
      const unit = data[sel.value];
      if (label) label.textContent = unit || '—';
    }catch(e){ console.warn('MaterialUnits row error', e); }
  };

  // ---------- events ----------
  btnAdd?.addEventListener('click', () => {
    const first = tbl.querySelector('tbody tr');
    const clone = first.cloneNode(true);

    clone.querySelector('.material-select').selectedIndex = 0;
    clone.querySelector('.diff-input').value = '0';
    clone.querySelector('.unit-label').textContent = '—';
    setOnhandBadge(clone.querySelector('.onhand-badge'), '—');

    tbl.querySelector('tbody').appendChild(clone);
    reindex();
    syncMaterialOptions();
  });

  tbl.addEventListener('click', (e) => {
    if (e.target.classList.contains('btn-del-row')) {
      const rows = tbl.querySelectorAll('tbody tr');
      if (rows.length > 1) {
        e.target.closest('tr').remove();
        reindex();
        syncMaterialOptions();
        refreshOnHand();
        refreshUnits({ force: true });
      }
    }
  });

  tbl.addEventListener('change', (e) => {
    if (e.target.classList.contains('material-select')) {
      syncMaterialOptions();
      fillUnitForRow(e.target.closest('tr'));
      refreshOnHand();
    }
  });

  tbl.addEventListener('input', (e) => {
    if (e.target.classList.contains('diff-input')) {
      validateRow(e.target.closest('tr'));
    }
  });

  whSel?.addEventListener('change', () => {
    refreshOnHand();
  });

  // ---------- init ----------
  syncMaterialOptions();
  refreshUnits({ force: false });
  refreshOnHand();
})();
