# ----------------------------
# build from source (React)
# ----------------------------
FROM node:18-alpine AS build
WORKDIR /app
COPY . .

# Set API base URL for build (OpenShift production URL)
ENV REACT_APP_API_BASE=https://azuredevops-aggregator/api

RUN npm ci --only=production
RUN npm run build

# Production stage with nginx unprivileged
FROM nginxinc/nginx-unprivileged:stable-alpine-perl
USER root
RUN apk update && \
    apk upgrade
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d
COPY --from=build /app/build /usr/share/nginx/html

USER nginx
EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"] 