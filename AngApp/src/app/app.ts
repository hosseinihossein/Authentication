import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { IconService } from './shared/icon-service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  iconService = inject(IconService);
}
