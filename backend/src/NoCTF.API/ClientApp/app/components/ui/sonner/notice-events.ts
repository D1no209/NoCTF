/** Toast actions must not dismiss the modal containing the field or operation they describe. */
export function keepNoticeInteractive(event: { detail: { originalEvent: Event }; preventDefault: () => void }) {
  const target = event.detail.originalEvent.target
  if (target instanceof Element && target.closest('[data-sonner-toaster]')) event.preventDefault()
}
