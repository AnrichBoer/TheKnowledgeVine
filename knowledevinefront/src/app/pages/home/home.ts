import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [CommonModule],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
 
  constructor(private route: Router) {
    
    
  }

  navtoContactUs(){
    this.route.navigateByUrl('contact');
  }

  

}
