import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { ReactiveFormsModule } from '@angular/forms';
import { FormBuilder, Validators } from '@angular/forms';


@Component({
  selector: 'app-contactus',
  imports: [
    CommonModule,
    MatIconModule,
    ReactiveFormsModule,
    MatInputModule,
    MatFormFieldModule,
    MatButtonModule,
  ],
  templateUrl: './contactus.html',
  styleUrl: './contactus.scss',
})
export class Contactus implements OnInit {
  contactForm: any;

  constructor(private fb: FormBuilder) {


  }

  ngOnInit() {

    this.contactForm = this.fb.group({
      name: ['', Validators.required],
      contactNumber: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      message: ['', Validators.required],
    });

  }

  submitForm() {

    if (this.contactForm.valid) {
      console.log(this.contactForm.value);

      

      // call your API here later
    }

  }
}
