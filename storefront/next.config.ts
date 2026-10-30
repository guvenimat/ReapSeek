import type { NextConfig } from 'next'

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

const config: NextConfig = {
  output: 'standalone',
  poweredByHeader: false,
  async rewrites() {
    return [
      { source: '/api/:path*', destination: `${apiUrl}/api/:path*` },
      { source: '/media/:path*', destination: `${apiUrl}/media/:path*` },
    ]
  },
}

export default config
