# Use Node.js 22 (Vite 7+ requires Node 20.19+ or 22.12+).
# STU-W-00: bumped from node:18 which could not run the Vite 7.1.12 build
# ('crypto.hash is not a function' was the symptom; Vite itself warned that
# Node 18 was below its minimum).
FROM node:22-alpine AS builder

# Set the working directory in the container
WORKDIR /app

# Copy package.json and yarn.lock to the container
COPY package.json yarn.lock* package-lock.json* pnpm-lock.yaml* ./

# Copy the rest of the application code
COPY . .

RUN \
  if [ -f yarn.lock ]; then yarn --frozen-lockfile; \
  elif [ -f package-lock.json ]; then npm i; \
  elif [ -f pnpm-lock.yaml ]; then yarn global add pnpm && pnpm i --frozen-lockfile; \
  else echo "Lockfile not found." && exit 1; \
  fi

# Build vue.js based on the preferred package manager
RUN \
  if [ -f yarn.lock ]; then yarn build; \
  elif [ -f package-lock.json ]; then npm run build; \
  elif [ -f pnpm-lock.yaml ]; then pnpm run build; \
  else yarn build; \
  fi

# Use Nginx as the production server
FROM nginx:stable-alpine

# Copy the custom Nginx configuration file
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Copy the built Vue.js files to the Nginx web server directory
COPY --from=builder /app/dist /usr/share/nginx/html

# Expose port 80 for Nginx
EXPOSE 80

# Start Nginx when the container runs
CMD ["nginx", "-g", "daemon off;"]
