type ConsentFieldsProps = {
  consent: boolean
  audioConsent: boolean
  onConsentChange: (value: boolean) => void
  onAudioConsentChange: (value: boolean) => void
}

export function ConsentFields({ consent, audioConsent, onConsentChange, onAudioConsentChange }: ConsentFieldsProps) {
  return <>
    <p className="privacy">Frames and any public caption are sent to OpenRouter and its selected AI provider. Optional audio is sent to an audio-capable provider. Only share content you have permission to use.</p>
    <label className="check"><input type="checkbox" checked={consent} onChange={e => onConsentChange(e.target.checked)} /> I consent to sending this video’s frames and public caption for analysis.</label>
    <label className="check"><input type="checkbox" checked={audioConsent} onChange={e => onAudioConsentChange(e.target.checked)} /> I also consent to sending this video’s audio.</label>
  </>
}
