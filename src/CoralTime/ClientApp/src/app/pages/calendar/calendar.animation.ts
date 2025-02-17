import {
	animate,
	animateChild,
	query,
	stagger,
	state,
	style,
	transition,
	trigger,
	AnimationTriggerMetadata
} from '@angular/animations';

export const ctCalendarAnimation: {
	readonly slideCalendar: AnimationTriggerMetadata;
} = {
	slideCalendar: trigger('slideCalendar', [
		state('hide', style({
			opacity: 0
		})),
		transition('left => void', [
			style({
				position: 'relative',
				left: 'calc(-100% - 5px)',
				opacity: 1
			}),
			animate('.5s ease',
				style({
					position: 'relative',
					left: '-130%',
					opacity: 0
				})
			)
		]),
		transition('void => left, hide => left', [
			style({
				position: 'relative',
				left: '30%',
				opacity: 0
			}),
			animate('.5s ease',
				style({
					left: '0',
					opacity: 1
				})
			),
			query('@*', animateChild(), {optional: true})
		]),
		transition('right => void', [
			style({
				position: 'relative',
				left: 'calc(-100% - 5px)',
				opacity: 1
			}),
			animate('.5s ease',
				style({
					position: 'relative',
					left: '-70%',
					opacity: 0
				})
			)
		]),
		transition('void => right, hide => right', [
			style({
				position: 'relative',
				left: '-30%',
				opacity: 0
			}),
			animate('.5s ease',
				style({
					left: '0',
					opacity: 1
				})
			),
			query('@*', animateChild(), {optional: true})
		])
	])
};

export const ctCalendarDayAnimation: {
	readonly slideCalendarTask: AnimationTriggerMetadata;
} = {
	slideCalendarTask: trigger('slideCalendarTask', [
		state('hide', style({
			opacity: 0
		})),
		transition('hide => show', [
			style({
				overflow: 'hidden',
				opacity: 1
			}),
			query('.ct-calendar-task', [
					style({
						transform: 'translateY(150px)',
						opacity: 0
					}),
					stagger(50, [
						animate('.5s ease',
							style({
								transform: 'translateY(0)',
								opacity: 1
							})
						)
					]),
				],
				{optional: true}
			)
		])
	])
};
