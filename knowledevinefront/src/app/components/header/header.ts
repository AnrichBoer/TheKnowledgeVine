import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-header',
  imports: [RouterLink, RouterLinkActive, MatIconModule],
  templateUrl: './header.html',
  styleUrl: './header.scss',
})
export class Header {
  readonly menuOpen = signal(false);
  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }
  closeMenu() {
    this.menuOpen.set(false);
  }
}
