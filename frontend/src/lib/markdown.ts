function escapeHtml(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;')
}

function inlineMarkdown(value: string) {
  return escapeHtml(value)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
}

export function renderMarkdown(markdown?: string | null) {
  if (!markdown?.trim()) return ''

  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  const html: string[] = []
  let inList = false

  for (const rawLine of lines) {
    const line = rawLine.trimEnd()
    if (!line.trim()) {
      if (inList) {
        html.push('</ul>')
        inList = false
      }
      continue
    }

    const heading = /^(#{1,3})\s+(.+)$/.exec(line)
    if (heading) {
      if (inList) {
        html.push('</ul>')
        inList = false
      }
      const level = heading[1].length + 2
      html.push(`<h${level}>${inlineMarkdown(heading[2])}</h${level}>`)
      continue
    }

    const bullet = /^[-*]\s+(.+)$/.exec(line)
    if (bullet) {
      if (!inList) {
        html.push('<ul>')
        inList = true
      }
      html.push(`<li>${inlineMarkdown(bullet[1])}</li>`)
      continue
    }

    if (inList) {
      html.push('</ul>')
      inList = false
    }
    html.push(`<p>${inlineMarkdown(line)}</p>`)
  }

  if (inList) html.push('</ul>')
  return html.join('')
}
