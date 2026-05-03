import DOMPurify from 'dompurify'

const purify = DOMPurify(window)

// Allow only safe HTML subset for educational content
purify.setConfig({
  ALLOWED_TAGS: ['b', 'i', 'em', 'strong', 'p', 'br', 'ul', 'ol', 'li',
    'span', 'div', 'sub', 'sup', 'table', 'tr', 'td', 'th', 'thead', 'tbody',
    'img', 'code', 'pre', 'blockquote', 'h1', 'h2', 'h3', 'h4'],
  ALLOWED_ATTR: ['class', 'style', 'src', 'alt', 'width', 'height', 'dir'],
  ALLOW_DATA_ATTR: false,
})

export function sanitizeHtml(dirty: string): string {
  return purify.sanitize(dirty)
}
