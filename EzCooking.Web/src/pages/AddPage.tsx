import type { FormEvent, RefObject } from 'react'
import { Button } from '../components/Button'
import { ConsentFields } from '../components/ConsentFields'

type AddPageProps = {
  url: string
  hasFile: boolean
  fileInput: RefObject<HTMLInputElement | null>
  consent: boolean
  audioConsent: boolean
  busy: boolean
  onUrl: (value: string) => void
  onFile: (file: File | null) => void
  onConsent: (value: boolean) => void
  onAudioConsent: (value: boolean) => void
  onCreate: (event: FormEvent<HTMLFormElement>) => void
}

export function AddPage({ url, hasFile, fileInput, consent, audioConsent, busy, onUrl, onFile, onConsent,
  onAudioConsent, onCreate }: AddPageProps) {
  return <section className="narrow page">
    <h1>Add a cooking video</h1><p>Paste a public reel link or upload a video you can use.</p>
    <form className="capture" onSubmit={onCreate}>
      <label htmlFor="url">Instagram reel link <small>(optional with upload)</small></label>
      <input id="url" type="url" value={url} onChange={e => onUrl(e.target.value)} placeholder="https://www.instagram.com/reel/…" />
      <div className="or">or upload a video</div><input ref={fileInput} aria-label="Upload a video" type="file" accept="video/*" onChange={e => onFile(e.target.files?.[0] ?? null)} />
      <ConsentFields consent={consent} audioConsent={audioConsent} onConsentChange={onConsent} onAudioConsentChange={onAudioConsent} />
      <Button type="submit" disabled={busy || !consent || (!url && !hasFile)}>{busy ? 'Adding…' : 'Create recipe'}</Button>
    </form>
  </section>
}
