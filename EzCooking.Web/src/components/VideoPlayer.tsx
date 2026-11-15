type VideoPlayerProps = {
  video: string
  cover: string
  title: string
  sourceUrl: string | null
  error: boolean
  onError: () => void
}

export function VideoPlayer({ video, cover, title, sourceUrl, error, onError }: VideoPlayerProps) {
  if (!video) {
    return <p className="fine">The saved video is unavailable.</p>
  }

  return <div className="recipe-player">
    <video controls preload="metadata" poster={cover || undefined} src={video} aria-label={`Original video for ${title}`} onError={onError}>Your browser cannot play this video.</video>
    {error && <p className="job-error" role="alert">The saved video could not be played. {sourceUrl ? 'Try the original Instagram reel.' : 'The local video may be unavailable.'}</p>}
  </div>
}
