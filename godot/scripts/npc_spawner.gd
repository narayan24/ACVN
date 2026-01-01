extends Node
class_name NPCSpawner

@export var game_controller
@export var seat_service: SeatService
@export var npc_scene: PackedScene

var min_gap := 1
var max_gap := 4
var burst_followup_chance := 0.45
var burst_max := 2
var next_spawn_in_actions := 0
var burst_remaining := 0

func _ready() -> void:
    _schedule_next_spawn()

## Should be called once per player action.
func on_action_advanced() -> void:
    if not _can_spawn_tonight():
        return
    if burst_remaining > 0:
        _spawn_one()
        burst_remaining -= 1
        return
    if next_spawn_in_actions > 0:
        next_spawn_in_actions -= 1
        return
    if _should_spawn():
        _spawn_one()
        _maybe_queue_burst()
        _schedule_next_spawn()
    else:
        _schedule_next_spawn()

func _can_spawn_tonight() -> bool:
    return game_controller == null or not game_controller.has_method("is_evening_over") or not game_controller.is_evening_over()

func _should_spawn() -> bool:
    if seat_service == null or seat_service.seat_manager == null:
        return false
    var free := seat_service.seat_manager.get_free_seats().size()
    if free <= 0:
        return false
    var target_occ := 0.6
    if game_controller and game_controller.has_method("get_target_occupancy"):
        target_occ = game_controller.get_target_occupancy()
    var total_seats := free + seat_service.seat_manager.occupied.size()
    var current_occ := float(total_seats - free) / max(1, total_seats)
    var jitter := randf_range(-0.05, 0.1)
    var should := current_occ < clamp(target_occ + jitter, 0.0, 1.0)
    return should

func _spawn_one() -> void:
    if seat_service == null:
        return
    var npc
    if npc_scene:
        npc = npc_scene.instantiate()
    var seat := seat_service.claim_free_seat(npc)
    if seat == null:
        return
    if game_controller and game_controller.has_method("on_npc_spawned"):
        game_controller.on_npc_spawned(npc, seat)

func _maybe_queue_burst() -> void:
    if randf() < burst_followup_chance:
        burst_remaining = randi_range(1, burst_max)
    else:
        burst_remaining = 0

func _schedule_next_spawn() -> void:
    var target_occ := 0.6
    if game_controller and game_controller.has_method("get_target_occupancy"):
        target_occ = game_controller.get_target_occupancy()
    var density := clamp(target_occ, 0.0, 1.0)
    var gap := int(lerp(float(max_gap), float(min_gap), density))
    next_spawn_in_actions = randi_range(max(1, gap), max_gap + 1)
