import { useEffect, useState } from 'react';
import { Field, PageHead } from '../components/ui';
import { useStore, useTenantData } from '../store/store';
import type { SecuritySettings } from '../types';

const NUMBERS: [keyof SecuritySettings, string, string][] = [
  ['passwordMinLength', 'Minimum password length', 'characters'],
  ['passwordExpiryDays', 'Password expiry', 'days'],
  ['maxFailedLogins', 'Failed logins before lockout', 'attempts'],
  ['lockoutMinutes', 'Lockout duration', 'minutes'],
  ['sessionTimeoutMinutes', 'Idle session timeout', 'minutes'],
  ['jwtExpiryMinutes', 'Access token (JWT) lifetime', 'minutes'],
  ['refreshTokenDays', 'Refresh token lifetime', 'days'],
];

export default function Settings() {
  const { saveSettings, toast } = useStore();
  const d = useTenantData();
  const [s, setS] = useState<SecuritySettings>(d.settings);
  useEffect(() => setS(d.settings), [d.settings]);
  if (!s) return <div className="empty">No settings for this tenant.</div>;

  const save = async () => {
    if (await saveSettings(s)) toast('Settings saved');
  };

  return (
    <>
      <PageHead
        title="Security Settings"
        subtitle={`Authentication and session policy for ${d.tenant.name}.`}
        actions={
          <button className="btn primary" onClick={save}>
            Save settings
          </button>
        }
      />
      <div className="grid-2">
        <div className="card">
          <div className="card-head">
            <h3>Passwords, lockout &amp; tokens</h3>
          </div>
          <div className="card-body grid-2">
            {NUMBERS.map(([k, label, unit]) => (
              <Field key={k} label={`${label} (${unit})`}>
                <input type="number" min={1} value={s[k] as number} onChange={(e) => setS({ ...s, [k]: Number(e.target.value) })} />
              </Field>
            ))}
          </div>
        </div>
        <div className="card">
          <div className="card-head">
            <h3>Multi-factor &amp; network</h3>
          </div>
          <div className="card-body stack">
            <label className="row">
              <input type="checkbox" checked={s.mfaRequiredForAdmins} onChange={(e) => setS({ ...s, mfaRequiredForAdmins: e.target.checked })} /> Require MFA for administrators
            </label>
            <label className="row">
              <input type="checkbox" checked={s.mfaRequiredOutsideOffice} onChange={(e) => setS({ ...s, mfaRequiredOutsideOffice: e.target.checked })} /> Step-up MFA when outside office network
            </label>
            <Field label="Trusted IP ranges (office network)" hint="Comma separated CIDR blocks; requests from these ranges are tagged env.network = Office LAN">
              <textarea rows={3} value={s.allowedIpRanges} onChange={(e) => setS({ ...s, allowedIpRanges: e.target.value })} />
            </Field>
          </div>
        </div>
      </div>
    </>
  );
}
