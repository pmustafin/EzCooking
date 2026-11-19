import type { Page, PollState, Recipe } from '../types'
import { Button } from '../components/Button'
import { ConsentFields } from '../components/ConsentFields'

type ProcessingPageProps = {
  jobs: Page<Recipe>
  pollStates: Record<string, PollState>
  showDismissed: boolean
  retryId: string | null
  consent: boolean
  audioConsent: boolean
  busy: boolean
  loading: boolean
  onRefresh: () => void
  onShowDismissed: (value: boolean) => void
  onMore: () => void
  onStartRetry: (id: string) => void
  onCancelRetry: () => void
  onRetry: (id: string) => void
  onDismiss: (id: string) => void
  onDelete: (id: string) => void
  onConsent: (value: boolean) => void
  onAudioConsent: (value: boolean) => void
}

export function ProcessingPage({ jobs, pollStates, showDismissed, retryId, consent, audioConsent, busy, loading,
  onRefresh, onShowDismissed, onMore, onStartRetry, onCancelRetry, onRetry, onDismiss, onDelete, onConsent, onAudioConsent }: ProcessingPageProps) {
  return <section className="narrow page">
    <p className="intro-note">Your videos</p><h1>Processing</h1><p>Track videos being prepared and resolve any that need attention. Refresh to see new videos sent from Telegram.</p>
    <div className="processing-toolbar"><Button variant="secondary" disabled={busy || loading} onClick={onRefresh}>{busy ? 'Please wait…' : 'Refresh videos'}</Button><span>Refresh shows the first page.</span></div>
    <div className="tabs">
      <Button variant={showDismissed ? 'secondary' : 'primary'} disabled={busy} onClick={() => onShowDismissed(false)}>Current</Button>
      <Button variant={showDismissed ? 'primary' : 'secondary'} disabled={busy} onClick={() => onShowDismissed(true)}>Dismissed</Button>
    </div>
    {loading ? <p role="status">Loading videos…</p> : jobs.items.length === 0 ? <p className="empty">{showDismissed ? 'No dismissed videos.' : <>No videos need attention. <a href="#/add">Add a video</a> or share a reel with your paired Telegram bot.</>}</p> :
      <div className="job-list">{jobs.items.map(job => <article className={`job${job.status === 'processing' ? ' job-processing' : ''}${job.status === 'processing' && !pollStates[job.id]?.unavailable ? ' job-live' : ''}`} key={job.id}>
        <div className="job-head"><strong aria-live="polite">{job.status === 'complete' ? 'Recipe ready' : job.status === 'delete_failed' ? 'Deletion needs attention' : job.status === 'deleting' ? 'Deleting' : job.status === 'failed' ? 'Needs attention' : job.status === 'processing' ? 'Preparing recipe' : 'Waiting to process'}</strong><span>{new Date(job.createdAt).toLocaleString()}</span></div>
        {job.sourceUrl ? <a className="job-source" href={job.sourceUrl} target="_blank" rel="noreferrer">{job.sourceUrl}</a> : <p>Uploaded video</p>}
        {['staging', 'queued', 'processing'].includes(job.status) && <div className="job-activity">
          <p className="activity-current" role="status">{job.status === 'staging' ? 'Preparing the upload.' : job.status === 'queued' ? 'In the queue. Videos are processed one at a time.' : job.activity?.at(-1)?.message ?? 'Waiting for the first activity update.'}</p>
          {job.status === 'processing' && job.activity && job.activity.length > 0 && <p className="activity-time">Last activity: {new Date(job.activity.at(-1)!.at).toLocaleTimeString()}</p>}
          <p role={pollStates[job.id]?.unavailable ? 'status' : undefined} className={pollStates[job.id]?.unavailable ? 'activity-disconnected' : 'activity-time'}>{pollStates[job.id]?.unavailable
            ? 'Live updates unavailable. The app will keep trying.'
            : pollStates[job.id]?.checkedAt ? `Status checked at ${new Date(pollStates[job.id].checkedAt!).toLocaleTimeString()}. Activity changes at processing milestones.` : 'Checking for updates…'}</p>
          {job.status === 'processing' && job.activity && job.activity.length > 1 && <details className="activity-history"><summary>Recent activity</summary><ol>{job.activity.map((event, index) => <li key={`${event.at}-${index}`}><time dateTime={event.at}>{new Date(event.at).toLocaleTimeString()}</time><span>{event.message}</span></li>)}</ol></details>}
        </div>}
        {job.status === 'complete' && <p><a href={`#/recipes/${job.id}`}>Open saved recipe</a></p>}
        {job.error && <div className="job-error"><strong>What happened</strong><p>{job.error}</p></div>}
        {job.status === 'failed' && <>{!showDismissed && <p className="fine">Retry after resolving the issue. To upload a replacement with the same link, permanently delete this failed video first, then <a href="#/add">add the replacement</a>.</p>}
          {!showDismissed && (retryId === job.id ? <div className="retry"><ConsentFields consent={consent} audioConsent={audioConsent} onConsentChange={onConsent} onAudioConsentChange={onAudioConsent} /><div className="actions"><Button disabled={busy || !consent} onClick={() => onRetry(job.id)}>Retry with consent</Button><Button variant="secondary" disabled={busy} onClick={onCancelRetry}>Cancel</Button></div></div> : <Button className="retry" disabled={busy} onClick={() => onStartRetry(job.id)}>Retry this video</Button>)}
          <div className="actions job-secondary-actions">
            <Button variant="secondary" disabled={busy} onClick={() => onDismiss(job.id)}>{showDismissed ? 'Restore' : 'Dismiss'}</Button>
            <Button variant="secondary" disabled={busy} onClick={() => onDelete(job.id)}>Delete video</Button>
          </div><p className="fine job-retention">{showDismissed ? 'Restoring returns this video to Current.' : 'Dismiss hides this video without deleting it.'} Delete permanently removes the video and its local photos.</p></>}
        {job.status === 'delete_failed' && <Button variant="secondary" disabled={busy} onClick={() => onDelete(job.id)}>Retry deletion</Button>}
      </article>)}</div>}
    {jobs.hasMore && <Button variant="secondary" className="load-more" disabled={busy} onClick={onMore}>Load more videos</Button>}
  </section>
}
