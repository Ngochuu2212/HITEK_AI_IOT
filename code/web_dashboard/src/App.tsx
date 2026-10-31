import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { AlertHubProvider } from "./realtime/AlertHub";
import { LookupProvider } from "./components/Lookups";
import { Layout } from "./components/Layout";
import { LoginPage } from "./pages/LoginPage";
import { DashboardPage } from "./pages/DashboardPage";
import { CamerasPage } from "./pages/CamerasPage";
import { AlertsPage } from "./pages/AlertsPage";
import { AlertDetailPage } from "./pages/AlertDetailPage";
import { DevicesPage } from "./pages/DevicesPage";
import { HistoryPage } from "./pages/HistoryPage";
import { LogsPage } from "./pages/LogsPage";

export function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Gate />
      </BrowserRouter>
    </AuthProvider>
  );
}

// Chưa đăng nhập thì chỉ có màn hình đăng nhập; kết nối realtime và danh mục chỉ mở sau khi có token.
function Gate() {
  const { user, loading } = useAuth();
  if (loading) return <div className="login"><p className="muted">Đang kiểm tra phiên đăng nhập…</p></div>;
  if (!user) return <LoginPage />;

  return (
    <AlertHubProvider>
      <LookupProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<DashboardPage />} />
            <Route path="cameras" element={<CamerasPage />} />
            <Route path="alerts" element={<AlertsPage />} />
            <Route path="alerts/:id" element={<AlertDetailPage />} />
            <Route path="devices" element={<DevicesPage />} />
            <Route path="history" element={<HistoryPage />} />
            <Route path="logs" element={<LogsPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </LookupProvider>
    </AlertHubProvider>
  );
}
