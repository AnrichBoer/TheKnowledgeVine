import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './header.html',
  styleUrl: './header.scss',
})
export class Header {
  menuOpen: boolean = false;

  constructor(private route: Router) {


  }

  toggleMenu() {
    this.menuOpen = !this.menuOpen;
  }

  navtoContactUs() {
    this.route.navigateByUrl('contact');
    this.menuOpen = false;
  }

  navParentPortal(){

    this.menuOpen = false;
  }

  navAboutUs(){
    this.route.navigateByUrl('about');
    this.menuOpen = false;
  }

  navHome() {
    this.route.navigateByUrl('home');
    this.menuOpen = false;
  }
}
