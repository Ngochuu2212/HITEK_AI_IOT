import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "../api/client";
import type { Area, Device } from "../api/types";

interface Lookups {
  areas: Area[];
  devices: Device[];
  areaName: (id: string | null | undefined) => string;
  deviceName: (id: string | null | undefined) => string;
  reload: () => void;
}

const LookupContext = createContext<Lookups | null>(null);

// Danh sách khu vực và thiết bị nạp một lần, để mọi trang hiện tên thay cho mã GUID.
export function LookupProvider({ children }: { children: ReactNode }) {
  const [areas, setAreas] = useState<Area[]>([]);
  const [devices, setDevices] = useState<Device[]>([]);

  const reload = useCallback(() => {
    api.areas().then(setAreas).catch(() => {});
    api.devices().then((r) => setDevices(r.items)).catch(() => {});
  }, []);

  useEffect(reload, [reload]);

  const areaName = useCallback((id: string | null | undefined) => areas.find((a) => a.id === id)?.name ?? (id ? id.slice(0, 8) : "—"), [areas]);
  const deviceName = useCallback((id: string | null | undefined) => devices.find((d) => d.id === id)?.name ?? (id ? id.slice(0, 8) : "—"), [devices]);

  return <LookupContext.Provider value={{ areas, devices, areaName, deviceName, reload }}>{children}</LookupContext.Provider>;
}

export function useLookups() {
  const ctx = useContext(LookupContext);
  if (!ctx) throw new Error("useLookups phải dùng bên trong LookupProvider");
  return ctx;
}
