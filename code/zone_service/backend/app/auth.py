"""FastAPI glue cho phan quyen: dependency lay Principal va router /api/me, dang nhap."""

import json
import urllib.error
import urllib.request
from typing import Optional

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from .access_control import AccessControl, AccessDenied, AuthError, Principal


def principal_dependency(access: AccessControl):
    def current_principal(request: Request) -> Principal:
        try:
            return access.authenticate(
                request.headers.get("authorization"),
                request.query_params.get("access_token"),
                request.headers.get("x-service-key"),
            )
        except AuthError as exc:
            raise HTTPException(status_code=401, detail=str(exc), headers={"WWW-Authenticate": "Bearer"})

    return current_principal


def access_denied_handler(request: Request, exc: AccessDenied) -> JSONResponse:
    return JSONResponse(status_code=exc.status_code, content={"detail": exc.detail})


class DevLoginIn(BaseModel):
    email: str


class LoginIn(BaseModel):
    email: str
    password: str


def _backend_login(backend_base_url: str, email: str, password: str) -> dict:
    """Dang nhap bang tai khoan cua Backend: Backend kiem tra mat khau va ky token, Zone Service
    chi chuyen tiep. Nho vay mot tai khoan dung chung cho Web Dashboard va dashboard nay."""
    request = urllib.request.Request(
        "{}/api/v1/auth/login".format(backend_base_url),
        data=json.dumps({"email": email, "password": password}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        try:
            message = json.loads(exc.read().decode("utf-8"))["error"]["message"]
        except Exception:
            message = "Đăng nhập thất bại ({}).".format(exc.code)
        # 400 chu khong 401: sai mat khau khong phai "phien het han".
        raise HTTPException(status_code=400 if exc.code in (400, 401, 403) else 502, detail=message)
    except Exception:
        raise HTTPException(status_code=502, detail="Không kết nối được Backend để kiểm tra tài khoản.")


def create_auth_router(access: AccessControl) -> APIRouter:
    router = APIRouter(tags=["auth"])
    current_principal = principal_dependency(access)

    @router.get("/api/me")
    def me(principal: Principal = Depends(current_principal)):
        locations = None
        if principal.locations is not None or principal.kind == "user":
            with access.database.session() as connection:
                rows = access.database.fetchall(connection, "SELECT id, name, code FROM location ORDER BY name", ())
            locations = [
                {"id": str(r["id"]), "name": r["name"], "code": r["code"], "level": principal.level_for(str(r["id"]))}
                for r in rows
                if principal.level_for(str(r["id"]))
            ]
        return {
            "auth_mode": access.settings.auth_mode,
            "kind": principal.kind,
            "user_id": principal.subject,
            "email": principal.email,
            "full_name": principal.full_name,
            "tenant_id": principal.tenant_id,
            "role": principal.role,
            "unrestricted": principal.is_unrestricted,
            "locations": locations,
        }

    @router.get("/api/auth/config")
    def auth_config():
        return {"auth_mode": access.settings.auth_mode, "dev_login": access.enabled and access.settings.auth_dev_login}

    @router.get("/api/auth/dev-users")
    def dev_users():
        return access.dev_users()

    @router.post("/api/auth/login")
    def login(payload: LoginIn):
        result = _backend_login(access.settings.backend_base_url, payload.email.strip(), payload.password)
        return {"access_token": result["accessToken"], "token_type": "Bearer"}

    @router.post("/api/auth/dev-login")
    def dev_login(payload: DevLoginIn):
        token = access.issue_dev_token(payload.email)
        return {"access_token": token, "token_type": "Bearer"}

    return router
