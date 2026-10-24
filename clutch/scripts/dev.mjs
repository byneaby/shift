import { spawn } from 'node:child_process'

const server = spawn('node', ['server/index.js'], { stdio: 'inherit' })
const client = spawn('npx', ['vite', '--config', 'client/vite.config.ts'], { stdio: 'inherit' })

function stop() {
  server.kill()
  client.kill()
  process.exit(0)
}

process.on('SIGINT', stop)
process.on('SIGTERM', stop)
server.on('exit', (code) => {
  if (code) {
    client.kill()
    process.exit(code)
  }
})
