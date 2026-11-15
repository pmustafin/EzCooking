export async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.detail || `Request failed (${response.status}).`)
  }
  return response.status === 204 ? undefined as T : response.json()
}

export const json = (body: unknown): RequestInit => ({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })

export const message = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong.'
