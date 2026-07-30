import { Component, computed, input } from '@angular/core';
import { MatProgressSpinner } from '@angular/material/progress-spinner';

type ProgressSpinnerMode = 'determinate' | 'indeterminate';

@Component({
  selector: 'app-wait-spinner',
  imports: [MatProgressSpinner],
  templateUrl: './wait-spinner.html',
  styleUrl: './wait-spinner.css'
})
export class WaitSpinner {
  value = input<number>(0);
  mode = computed(()=>this.value() > 0 ? "determinate" : "indeterminate");
}
