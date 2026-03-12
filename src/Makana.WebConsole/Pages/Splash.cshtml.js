/**
 * Universal Copy Button
 * Adds a "Copy" button to all elements with the "copyme" class.
 * The button copies the element's text content to the clipboard.
 */
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('.copyme').forEach(element => {
    // Create the button
    const button = document.createElement('button');
    button.className = 'ghost small';
    button.textContent = 'Copy';
    button.setAttribute('aria-label', 'Copy to clipboard');
    button.type = 'button';

    // Handle click
    button.addEventListener('click', () => {
      const text = element.textContent.trim();
      navigator.clipboard.writeText(text).then(() => {
        // Show feedback
        const originalText = button.textContent;
        button.textContent = 'Copied!';
        setTimeout(() => {
          button.textContent = originalText;
        }, 2000);
      }).catch(err => {
        console.error('Failed to copy:', err);
        button.textContent = 'Error';
        setTimeout(() => {
          button.textContent = 'Copy';
        }, 2000);
      });
    });

    // Insert button before the element
    element.parentNode.insertBefore(button, element);
  });
});
