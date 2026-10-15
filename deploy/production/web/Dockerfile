# Keep the already-running production nginx binary; replace only this app's assets/config.
ARG WEB_BASE=nginx:alpine
FROM ${WEB_BASE}
RUN rm -rf /usr/share/nginx/html/*
COPY browser/ /usr/share/nginx/html/
COPY nginx.conf /etc/nginx/conf.d/default.conf
