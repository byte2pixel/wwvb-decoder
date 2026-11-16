/** Switches between the dark and light themes. `<html>` carries the `dark` class. */
export function toggleTheme(): void {
  document.documentElement.classList.toggle("dark");
}
