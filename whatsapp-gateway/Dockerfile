FROM node:20-alpine
WORKDIR /app

# Install dependencies
COPY package*.json ./
RUN apk add --no-cache git && npm install

# Copy source code
COPY . .

EXPOSE 3000
CMD ["npm", "start"]
