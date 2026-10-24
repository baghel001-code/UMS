import { useState } from 'react';
import { Badge, Field, Modal, PageHead, fmtDate } from '../components/ui';
import { REGIONS } from '../data/seed';
import { isInSchedule } from '../engine/pdp';
import { newId, useStore, useTenantData } from '../store/store';
import { WEEKDAYS, type Holiday, type Schedule, type TimeWindow } from '../types';

// a Monday, used to render the weekly grid
const BASE = new Date('2026-10-19T00:00');

function WeekGrid({ schedule }: { schedule: Schedule }) {
  return (
    <div className="week" aria-label={`${schedule.name} weekly grid`}>
      <span />
      {Array.from({ length: 24 }, (_, h) => (
        <span key={h} className="h">
          {h % 3 === 0 ? h : ''}
        </span>
      ))}
      {WEEKDAYS.map((day, di) => [
        <span key={day} className="d">
          {day}
        </span>,
        ...Array.from({ length: 24 }, (_, h) => {
          const at = new Date(BASE);
          at.setDate(BASE.getDate() + di);
          at.setHours(h, 30);
          // ignore holidays so the grid shows the regular weekly pattern
          const on = isInSchedule({ ...schedule, excludeHolidays: false }, at, []);
          return <span key={`${day}${h}`} className={`c ${on ? 'on' : ''}`} title={`${day} ${h}:30`} />;
        }),
      ])}
    </div>
  );
}

export default function Schedules() {
  const { tenantId, upsert, remove, toast } = useStore();
  const d = useTenantData();
  const [editing, setEditing] = useState<Schedule | null>(null);
  const [hol, setHol] = useState<Holiday>({ id: '', tenantId, date: '', name: '', regions: [] });

  const usedBy = (s: Schedule) => d.policies.filter((p) => p.conditions.some((c) => c.value === s.id && c.operator.includes('Schedule')));

  return (
    <>
      <PageHead
        title="Office Hours & Holidays"
        subtitle="Named time windows used by ABAC policies (e.g. “Payroll only during General Office Hours”). Holidays can close a schedule for all or specific regions."
        actions={
          <button
            className="btn primary"
            onClick={() =>
              setEditing({ id: newId('sch'), tenantId, code: '', name: '', timezone: 'Asia/Kolkata', excludeHolidays: true, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'], start: '09:30', end: '18:30' }] })
            }
          >
            + New schedule
          </button>
        }
      />
      <div className="grid-2" style={{ marginBottom: 16 }}>
        {d.schedules.map((s) => (
          <div key={s.id} className="card">
            <div className="card-head">
              <div>
                <h3>{s.name}</h3>
                <div className="small muted">
                  <code>{s.code}</code> · {s.timezone} · {s.excludeHolidays ? 'closed on holidays' : 'open on holidays'}
                </div>
              </div>
              <button className="btn sm" onClick={() => setEditing(structuredClone(s))}>
                Edit
              </button>
            </div>
            <div className="card-body">
              <WeekGrid schedule={s} />
              <div className="small" style={{ marginTop: 10 }}>
                {s.windows.map((w, i) => (
                  <div key={i}>
                    {w.days.join(', ')} · {w.start}–{w.end}
                    {w.end < w.start && <Badge>overnight</Badge>}
                  </div>
                ))}
              </div>
              <div className="small muted" style={{ marginTop: 6 }}>
                Used by: {usedBy(s).map((p) => p.name).join('; ') || 'no policy'}
              </div>
            </div>
          </div>
        ))}
        {d.schedules.length === 0 && <div className="card empty">No schedules for this tenant.</div>}
      </div>

      <div className="card">
        <div className="card-head">
          <h2>Holiday calendar</h2>
        </div>
        <div className="card-body">
          <div className="row" style={{ marginBottom: 12 }}>
            <input type="date" value={hol.date} onChange={(e) => setHol({ ...hol, date: e.target.value })} />
            <input placeholder="Holiday name" value={hol.name} onChange={(e) => setHol({ ...hol, name: e.target.value })} />
            {REGIONS.map((r) => (
              <label key={r} className="row small">
                <input type="checkbox" checked={hol.regions.includes(r)} onChange={(e) => setHol({ ...hol, regions: e.target.checked ? [...hol.regions, r] : hol.regions.filter((x) => x !== r) })} />
                {r}
              </label>
            ))}
            <button
              className="btn"
              onClick={() => {
                if (!hol.date || !hol.name) return toast('Date and name are required', 'error');
                upsert('holidays', { ...hol, id: newId('hol'), tenantId }, `Added holiday ${hol.name} on ${hol.date}`);
                setHol({ id: '', tenantId, date: '', name: '', regions: [] });
              }}
            >
              + Add holiday
            </button>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Holiday</th>
                  <th>Regions</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {[...d.holidays]
                  .sort((a, b) => a.date.localeCompare(b.date))
                  .map((h) => (
                    <tr key={h.id}>
                      <td>{fmtDate(h.date)}</td>
                      <td>{h.name}</td>
                      <td>{h.regions.length ? h.regions.map((r) => <span key={r} className="chip">{r}</span>) : <Badge>All India</Badge>}</td>
                      <td className="right">
                        <button className="btn sm danger" onClick={() => remove('holidays', h.id, `Removed holiday ${h.name}`)}>
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
            {d.holidays.length === 0 && <div className="empty">No holidays defined.</div>}
          </div>
        </div>
      </div>
      {editing && <ScheduleEditor schedule={editing} onClose={() => setEditing(null)} inUse={usedBy(editing).length} />}
    </>
  );
}

function ScheduleEditor({ schedule: initial, onClose, inUse }: { schedule: Schedule; onClose: () => void; inUse: number }) {
  const { state, upsert, remove, toast } = useStore();
  const [s, setS] = useState(initial);
  const isNew = !state.schedules.some((x) => x.id === s.id);
  const setW = (i: number, w: Partial<TimeWindow>) => setS({ ...s, windows: s.windows.map((x, j) => (j === i ? { ...x, ...w } : x)) });

  return (
    <Modal
      title={isNew ? 'New schedule' : `Schedule: ${s.name}`}
      onClose={onClose}
      footer={
        <>
          {!isNew && (
            <button
              className="btn danger"
              disabled={inUse > 0}
              title={inUse ? 'Used by policies' : ''}
              onClick={() => {
                remove('schedules', s.id);
                onClose();
              }}
            >
              Delete
            </button>
          )}
          <div className="grow" />
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn primary"
            onClick={() => {
              if (!s.code || !s.name) return toast('Code and name are required', 'error');
              if (s.windows.some((w) => w.days.length === 0)) return toast('Each window needs at least one day', 'error');
              upsert('schedules', s);
              toast('Schedule saved');
              onClose();
            }}
          >
            Save
          </button>
        </>
      }
    >
      <div className="grid-2" style={{ marginBottom: 12 }}>
        <Field label="Code">
          <input value={s.code} onChange={(e) => setS({ ...s, code: e.target.value.toUpperCase().replace(/\s+/g, '_') })} />
        </Field>
        <Field label="Name">
          <input value={s.name} onChange={(e) => setS({ ...s, name: e.target.value })} />
        </Field>
        <Field label="Time zone">
          <select value={s.timezone} onChange={(e) => setS({ ...s, timezone: e.target.value })}>
            <option>Asia/Kolkata</option>
            <option>Asia/Dubai</option>
            <option>UTC</option>
          </select>
        </Field>
        <Field label="Holidays">
          <label className="row">
            <input type="checkbox" checked={s.excludeHolidays} onChange={(e) => setS({ ...s, excludeHolidays: e.target.checked })} /> Closed on holidays
          </label>
        </Field>
      </div>
      <h3 style={{ marginBottom: 8 }}>Time windows</h3>
      <div className="stack">
        {s.windows.map((w, i) => (
          <div key={i} className="card card-body">
            <div className="row" style={{ marginBottom: 8 }}>
              {WEEKDAYS.map((day) => (
                <label key={day} className="row small">
                  <input type="checkbox" checked={w.days.includes(day)} onChange={(e) => setW(i, { days: e.target.checked ? WEEKDAYS.filter((x) => w.days.includes(x) || x === day) : w.days.filter((x) => x !== day) })} />
                  {day}
                </label>
              ))}
            </div>
            <div className="row">
              <input type="time" value={w.start} onChange={(e) => setW(i, { start: e.target.value })} />
              <span>to</span>
              <input type="time" value={w.end} onChange={(e) => setW(i, { end: e.target.value })} />
              {w.end < w.start && <Badge>overnight — ends next day</Badge>}
              <span className="grow" />
              <button className="btn sm danger" onClick={() => setS({ ...s, windows: s.windows.filter((_, j) => j !== i) })}>
                Remove
              </button>
            </div>
          </div>
        ))}
        <div>
          <button className="btn sm" onClick={() => setS({ ...s, windows: [...s.windows, { days: ['Sat'], start: '10:00', end: '14:00' }] })}>
            + Add window
          </button>
        </div>
        <WeekGrid schedule={s} />
      </div>
    </Modal>
  );
}
