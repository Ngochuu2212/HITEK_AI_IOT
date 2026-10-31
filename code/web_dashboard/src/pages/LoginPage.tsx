import { useState, type FormEvent } from "react";
import { useAuth } from "../auth/AuthContext";
import { ApiError } from "../api/client";

const DEMO = ["admin@demo.local", "kho.operator@demo.local", "cong.viewer@demo.local"];

export function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Không kết nối được Backend API.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login">
      <form className="login-card" onSubmit={submit}>
        <h1>HITEK AI-IoT</h1>
        <p>Hệ thống giám sát và cảnh báo bất thường từ camera và cảm biến</p>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
        </label>
        <label>
          Mật khẩu
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && <div className="error-box">{error}</div>}
        <button className="primary" disabled={busy}>{busy ? "Đang đăng nhập…" : "Đăng nhập"}</button>
        {(import.meta.env.DEV || import.meta.env.VITE_SHOW_DEMO_ACCOUNTS === "true") && (
          <div className="demo-accounts">
            Tài khoản demo (mật khẩu <code>Demo@123</code>):
            {DEMO.map((d) => (
              <button type="button" key={d} className="link" onClick={() => { setEmail(d); setPassword("Demo@123"); }}>{d}</button>
            ))}
          </div>
        )}
      </form>
    </div>
  );
}
