// =============================================================================
// Cena Adaptive Learning Platform — Knowledge Graph Notifier
// Manages the interactive knowledge graph with zoom, pan, and node selection.
// =============================================================================

import 'dart:async';
import 'dart:collection';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/websocket_service.dart';
import 'derived_providers.dart' show webSocketServiceProvider;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/// Immutable snapshot of the knowledge graph visualization state.
class KnowledgeGraphState {
  const KnowledgeGraphState({
    this.graph,
    this.selectedNodeId,
    this.subjectFilter,
    this.zoomLevel = 1.0,
    this.panOffsetX = 0.0,
    this.panOffsetY = 0.0,
    this.isLoading = false,
    this.error,
    this.searchQuery,
    this.highlightedPath = const [],
  });

  final KnowledgeGraph? graph;
  final String? selectedNodeId;
  final Subject? subjectFilter;

  /// Current zoom factor, clamped to [0.3, 3.0].
  final double zoomLevel;
  final double panOffsetX;
  final double panOffsetY;
  final bool isLoading;
  final String? error;

  /// Hebrew search query for filtering concept names.
  final String? searchQuery;

  /// Concept IDs along the highlighted prerequisite path to selectedNodeId.
  final List<String> highlightedPath;

  /// The currently selected node, derived from selectedNodeId.
  ConceptNode? get selectedNode => graph?.nodes
      .cast<ConceptNode?>()
      .firstWhere((n) => n?.conceptId == selectedNodeId, orElse: () => null);

  /// Nodes after applying subject filter and search query.
  List<ConceptNode> get filteredNodes {
    if (graph == null) return const [];
    Iterable<ConceptNode> nodes = graph!.nodes;

    if (subjectFilter != null) {
      nodes = nodes.where((n) => n.subject == subjectFilter);
    }

    final query = searchQuery?.trim().toLowerCase();
    if (query != null && query.isNotEmpty) {
      nodes = nodes.where((n) {
        final labelMatch = n.label.toLowerCase().contains(query);
        final heMatch = n.labelHe?.toLowerCase().contains(query) ?? false;
        return labelMatch || heMatch;
      });
    }

    return nodes.toList();
  }

  KnowledgeGraphState copyWith({
    KnowledgeGraph? graph,
    String? selectedNodeId,
    Subject? subjectFilter,
    double? zoomLevel,
    double? panOffsetX,
    double? panOffsetY,
    bool? isLoading,
    String? error,
    String? searchQuery,
    List<String>? highlightedPath,
    bool clearSelectedNode = false,
    bool clearSubjectFilter = false,
    bool clearSearchQuery = false,
  }) {
    return KnowledgeGraphState(
      graph: graph ?? this.graph,
      selectedNodeId:
          clearSelectedNode ? null : (selectedNodeId ?? this.selectedNodeId),
      subjectFilter:
          clearSubjectFilter ? null : (subjectFilter ?? this.subjectFilter),
      zoomLevel: zoomLevel ?? this.zoomLevel,
      panOffsetX: panOffsetX ?? this.panOffsetX,
      panOffsetY: panOffsetY ?? this.panOffsetY,
      isLoading: isLoading ?? this.isLoading,
      error: error ?? this.error,
      searchQuery:
          clearSearchQuery ? null : (searchQuery ?? this.searchQuery),
      highlightedPath: highlightedPath ?? this.highlightedPath,
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Manages the knowledge graph visualization state.
///
/// Handles user gestures (zoom, pan, node selection) and receives live graph
/// updates from the WebSocket service when mastery changes.
class KnowledgeGraphNotifier extends StateNotifier<KnowledgeGraphState> {
  KnowledgeGraphNotifier({required this.webSocketService})
      : super(const KnowledgeGraphState()) {
    _subscribeToEvents();
  }

  final WebSocketService webSocketService;
  StreamSubscription<MessageEnvelope>? _subscription;

  // ---- Public API ----

  /// Select a concept node; tap the same node again to deselect.
  void selectNode(String? conceptId) {
    if (conceptId != null && conceptId == state.selectedNodeId) {
      // Tapping the already-selected node deselects it.
      state = state.copyWith(
        clearSelectedNode: true,
        highlightedPath: const [],
      );
    } else {
      state = state.copyWith(selectedNodeId: conceptId);
      if (conceptId != null) {
        highlightPathTo(conceptId);
      }
    }
  }

  /// Filter the graph to show only concepts for one subject.
  /// Pass null to clear the filter.
  void filterBySubject(Subject? subject) {
    if (subject == null) {
      state = state.copyWith(clearSubjectFilter: true);
    } else {
      state = state.copyWith(subjectFilter: subject);
    }
  }

  /// Set zoom level, clamped to [0.3, 3.0].
  void setZoom(double zoom) {
    state = state.copyWith(zoomLevel: zoom.clamp(0.3, 3.0));
  }

  /// Update absolute pan offset from gesture detector.
  void setPanOffset(double x, double y) {
    state = state.copyWith(panOffsetX: x, panOffsetY: y);
  }

  /// Filter visible nodes by concept name (Hebrew or English).
  /// Pass null or empty string to clear the search.
  void search(String? query) {
    if (query == null || query.trim().isEmpty) {
      state = state.copyWith(clearSearchQuery: true);
    } else {
      state = state.copyWith(searchQuery: query.trim());
    }
  }

  /// BFS from every root concept to find all nodes on paths leading to
  /// [conceptId], then highlight those paths.
  void highlightPathTo(String conceptId) {
    final graph = state.graph;
    if (graph == null) {
      state = state.copyWith(highlightedPath: [conceptId]);
      return;
    }

    // Build adjacency: prerequisiteId -> [dependentConceptIds]
    final dependents = <String, List<String>>{};
    for (final edge in graph.edges) {
      dependents.putIfAbsent(edge.fromConceptId, () => []).add(edge.toConceptId);
    }

    // Build reverse: conceptId -> [prerequisiteIds]
    final prerequisites = <String, List<String>>{};
    for (final edge in graph.edges) {
      prerequisites.putIfAbsent(edge.toConceptId, () => []).add(edge.fromConceptId);
    }

    // BFS backwards from target to find all ancestors on shortest paths.
    final path = <String>{conceptId};
    final queue = Queue<String>()..add(conceptId);
    while (queue.isNotEmpty) {
      final current = queue.removeFirst();
      for (final prereq in prerequisites[current] ?? const <String>[]) {
        if (path.add(prereq)) {
          queue.add(prereq);
        }
      }
    }

    state = state.copyWith(highlightedPath: path.toList());
  }

  /// Clear the highlighted prerequisite path.
  void clearHighlight() {
    state = state.copyWith(highlightedPath: const []);
  }

  // ---- WebSocket event routing ----

  void _subscribeToEvents() {
    _subscription = webSocketService.messageStream.listen(_handleMessage);
  }

  void _handleMessage(MessageEnvelope envelope) {
    if (envelope.type == 'KnowledgeGraphUpdated') {
      _onGraphUpdated(envelope.payload);
    }
  }

  void _onGraphUpdated(Map<String, dynamic> payload) {
    final graph =
        KnowledgeGraph.fromJson(payload['graph'] as Map<String, dynamic>);
    state = state.copyWith(graph: graph, isLoading: false);
  }

  @override
  void dispose() {
    _subscription?.cancel();
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/// Knowledge graph state provider — auto-disposed when no listeners.
final knowledgeGraphProvider = StateNotifierProvider.autoDispose<
    KnowledgeGraphNotifier, KnowledgeGraphState>((ref) {
  return KnowledgeGraphNotifier(
    webSocketService: ref.watch(webSocketServiceProvider),
  );
});
