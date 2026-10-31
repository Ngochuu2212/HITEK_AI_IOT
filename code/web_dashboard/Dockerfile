# Bước 1: build bản tĩnh bằng Node.
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Hiện danh sách tài khoản demo ở màn hình đăng nhập (chỉ dùng cho bản chạy thử).
ARG VITE_SHOW_DEMO_ACCOUNTS=true
ENV VITE_SHOW_DEMO_ACCOUNTS=$VITE_SHOW_DEMO_ACCOUNTS
RUN npm run build

# Bước 2: Nginx phục vụ trang và làm proxy cùng nguồn tới Backend, Zone Service, AI Service.
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
