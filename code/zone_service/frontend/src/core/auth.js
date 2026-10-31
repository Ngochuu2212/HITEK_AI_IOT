// Dang nhap: lay cau hinh, user hien tai, man hinh dang nhap, nut dang xuat.
import { api, getToken, setToken } from "./api.js";
import { LEVEL_LABELS, ROLE_LABELS, escapeHtml } from "./format.js";

export async function getAuthConfig() {
  return api("/api/auth/config").catch(() => ({ auth_mode: "off", dev_login: false }));
}

/** Tra ve thong tin user, hoac null neu can dang nhap. */
export async function fetchMe(config) {
  if (config.auth_mode !== "jwt") {
    return api("/api/me").catch(() => ({ kind: "anonymous", unrestricted: true, locations: null }));
  }
  if (!getToken()) return null;
  try {
    return await api("/api/me");
  } catch (error) {
    setToken(null);
    return null;
  }
}

export function renderUserChip(container, me, config) {
  if (config.auth_mode !== "jwt") {
    container.innerHTML = `<span class="user-chip" title="AUTH_MODE=off">Không bật đăng nhập</span>`;
    return;
  }
  const name = me.full_name || me.email || me.user_id;
  const role = ROLE_LABELS[me.role] || me.role || "";
  const places = (me.locations || []).map((loc) => `${loc.name}: ${LEVEL_LABELS[loc.level] || loc.level}`).join("\n");
  container.innerHTML = `
    <span class="user-chip" title="${escapeHtml(places || "Chưa được giao khu vực nào")}">
      <span class="user-name">${escapeHtml(name)}</span>
      <span class="user-role">${escapeHtml(role)}</span>
    </span>
    <button type="button" class="ghost small" data-logout>Đăng xuất</button>`;
  container.querySelector("[data-logout]").addEventListener("click", () => {
    setToken(null);
    window.location.reload();
  });
}

export async function renderLoginScreen(container, config) {
  // Tai khoan va mat khau la cua Backend; Zone Service chuyen tiep sang Backend kiem tra.
  const users = config.dev_login ? await api("/api/auth/dev-users").catch(() => []) : [];
  container.innerHTML = `
    <form class="login" id="loginForm">
      <h1>Đăng nhập</h1>
      <p class="muted">Dùng chung tài khoản với Web Dashboard. Mỗi tài khoản chỉ thấy camera của khu vực được giao.</p>
      <label class="field">
        <span>Email</span>
        <input id="loginEmail" type="email" autocomplete="username" required autofocus>
      </label>
      <label class="field">
        <span>Mật khẩu</span>
        <input id="loginPassword" type="password" autocomplete="current-password" required>
      </label>
      <button type="submit" class="primary" id="loginSubmit">Đăng nhập</button>
      <p class="form-error" id="loginError" hidden></p>
      ${
        users.length
          ? `<div class="demo-accounts">
               <span>Tài khoản demo (mật khẩu <code>Demo@123</code>), bấm để điền:</span>
               ${users
                 .map((u) => `<button type="button" class="link" data-email="${escapeHtml(u.email)}">${escapeHtml(u.email)} · ${escapeHtml(ROLE_LABELS[u.role] || u.role)}</button>`)
                 .join("")}
             </div>`
          : ""
      }
    </form>`;
  const form = container.querySelector("#loginForm");
  const email = container.querySelector("#loginEmail");
  const password = container.querySelector("#loginPassword");
  const submit = container.querySelector("#loginSubmit");
  const error = container.querySelector("#loginError");

  container.querySelectorAll("[data-email]").forEach((button) =>
    button.addEventListener("click", () => {
      email.value = button.dataset.email;
      password.value = "Demo@123";
      password.focus();
    }),
  );

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    error.hidden = true;
    submit.disabled = true;
    submit.textContent = "Đang đăng nhập…";
    try {
      const result = await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email: email.value.trim(), password: password.value }),
      });
      setToken(result.access_token);
      window.location.reload();
    } catch (err) {
      error.hidden = false;
      error.textContent = err.message || "Không đăng nhập được.";
      submit.disabled = false;
      submit.textContent = "Đăng nhập";
    }
  });
}
