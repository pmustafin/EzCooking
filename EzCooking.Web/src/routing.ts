export type Route = { page: 'collection' | 'add' | 'processing' | 'detail'; id?: string }

export function routeFromHash(): Route {
  const hash = window.location.hash
  if (hash === '#/add') return { page: 'add' }
  if (hash === '#/processing') return { page: 'processing' }
  const match = hash.match(/^#\/recipes\/([a-f0-9-]+)$/i)
  return match ? { page: 'detail', id: match[1] } : { page: 'collection' }
}
