import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/content/*` and `/api/knowledge/*`
 * endpoint groups from STB-08 Phase 1.
 *
 * STB-08b will replace the backend stub with a real ConceptDocument
 * seeded from knowledge-seed.json. The TypeScript/MSW side keeps a
 * parallel hardcoded catalog so the dev loop works without Cena.Api.Host.
 */

interface Concept {
  conceptId: string
  name: string
  description: string
  subject: string
  topic: string | null
  difficulty: 'beginner' | 'intermediate' | 'advanced'
  status: 'locked' | 'available' | 'in-progress' | 'mastered'
  currentMastery: number | null
  prerequisites: string[]
  estimatedMinutes: number
  questionCount: number
}

const CATALOG: Concept[] = [
  // Math
  { conceptId: 'math-arith', name: 'Arithmetic Basics', description: 'Addition, subtraction, multiplication, and division.', subject: 'math', topic: 'foundations', difficulty: 'beginner', status: 'mastered', currentMastery: 0.92, prerequisites: [], estimatedMinutes: 30, questionCount: 24 },
  { conceptId: 'math-fractions', name: 'Fractions & Decimals', description: 'Working with rational numbers.', subject: 'math', topic: 'foundations', difficulty: 'beginner', status: 'mastered', currentMastery: 0.88, prerequisites: ['math-arith'], estimatedMinutes: 40, questionCount: 32 },
  { conceptId: 'math-algebra', name: 'Linear Algebra', description: 'Variables, expressions, and linear equations.', subject: 'math', topic: 'algebra', difficulty: 'intermediate', status: 'in-progress', currentMastery: 0.55, prerequisites: ['math-fractions'], estimatedMinutes: 60, questionCount: 48 },
  { conceptId: 'math-quadratics', name: 'Quadratic Equations', description: 'Solving ax² + bx + c = 0.', subject: 'math', topic: 'algebra', difficulty: 'intermediate', status: 'available', currentMastery: null, prerequisites: ['math-algebra'], estimatedMinutes: 45, questionCount: 30 },
  { conceptId: 'math-geometry', name: 'Plane Geometry', description: 'Shapes, angles, and the Pythagorean theorem.', subject: 'math', topic: 'geometry', difficulty: 'intermediate', status: 'available', currentMastery: null, prerequisites: ['math-algebra'], estimatedMinutes: 50, questionCount: 36 },
  { conceptId: 'math-trig', name: 'Trigonometry', description: 'Sine, cosine, tangent, and the unit circle.', subject: 'math', topic: 'geometry', difficulty: 'advanced', status: 'locked', currentMastery: null, prerequisites: ['math-geometry'], estimatedMinutes: 70, questionCount: 42 },
  { conceptId: 'math-calculus', name: 'Introduction to Calculus', description: 'Limits, derivatives, and integrals.', subject: 'math', topic: 'calculus', difficulty: 'advanced', status: 'locked', currentMastery: null, prerequisites: ['math-quadratics', 'math-trig'], estimatedMinutes: 90, questionCount: 50 },
  // Physics
  { conceptId: 'physics-kinematics', name: 'Kinematics', description: 'Motion in a straight line: position, velocity, acceleration.', subject: 'physics', topic: 'mechanics', difficulty: 'beginner', status: 'in-progress', currentMastery: 0.42, prerequisites: [], estimatedMinutes: 55, questionCount: 36 },
  { conceptId: 'physics-forces', name: 'Newton\'s Laws', description: 'Force, mass, and acceleration.', subject: 'physics', topic: 'mechanics', difficulty: 'intermediate', status: 'available', currentMastery: null, prerequisites: ['physics-kinematics'], estimatedMinutes: 60, questionCount: 40 },
  { conceptId: 'physics-energy', name: 'Energy & Work', description: 'Kinetic, potential, and conservation of energy.', subject: 'physics', topic: 'mechanics', difficulty: 'intermediate', status: 'locked', currentMastery: null, prerequisites: ['physics-forces'], estimatedMinutes: 55, questionCount: 34 },
  { conceptId: 'physics-waves', name: 'Waves & Sound', description: 'Wavelength, frequency, interference.', subject: 'physics', topic: 'waves', difficulty: 'advanced', status: 'locked', currentMastery: null, prerequisites: ['physics-energy'], estimatedMinutes: 65, questionCount: 38 },
  // Chemistry
  { conceptId: 'chem-atoms', name: 'Atomic Structure', description: 'Protons, neutrons, electrons, and the periodic table.', subject: 'chemistry', topic: 'atoms', difficulty: 'beginner', status: 'mastered', currentMastery: 0.95, prerequisites: [], estimatedMinutes: 45, questionCount: 30 },
  { conceptId: 'chem-bonds', name: 'Chemical Bonding', description: 'Ionic, covalent, and metallic bonds.', subject: 'chemistry', topic: 'bonding', difficulty: 'intermediate', status: 'available', currentMastery: null, prerequisites: ['chem-atoms'], estimatedMinutes: 50, questionCount: 32 },
  { conceptId: 'chem-reactions', name: 'Chemical Reactions', description: 'Balancing equations and reaction types.', subject: 'chemistry', topic: 'reactions', difficulty: 'intermediate', status: 'locked', currentMastery: null, prerequisites: ['chem-bonds'], estimatedMinutes: 55, questionCount: 36 },
  { conceptId: 'chem-organic', name: 'Organic Chemistry Intro', description: 'Hydrocarbons and functional groups.', subject: 'chemistry', topic: 'organic', difficulty: 'advanced', status: 'locked', currentMastery: null, prerequisites: ['chem-reactions'], estimatedMinutes: 75, questionCount: 44 },
  // Biology
  { conceptId: 'bio-cells', name: 'Cell Biology', description: 'Cell structure and function.', subject: 'biology', topic: 'cells', difficulty: 'beginner', status: 'available', currentMastery: null, prerequisites: [], estimatedMinutes: 50, questionCount: 32 },
  { conceptId: 'bio-genetics', name: 'Genetics Basics', description: 'DNA, genes, and heredity.', subject: 'biology', topic: 'genetics', difficulty: 'intermediate', status: 'locked', currentMastery: null, prerequisites: ['bio-cells'], estimatedMinutes: 60, questionCount: 38 },
  { conceptId: 'bio-ecology', name: 'Ecology', description: 'Ecosystems, food webs, and energy flow.', subject: 'biology', topic: 'ecology', difficulty: 'intermediate', status: 'locked', currentMastery: null, prerequisites: ['bio-cells'], estimatedMinutes: 55, questionCount: 34 },
]

function dependenciesOf(conceptId: string): string[] {
  return CATALOG
    .filter(c => c.prerequisites.includes(conceptId))
    .map(c => c.conceptId)
}

export const handlerStudentKnowledge = [
  http.get('/api/content/concepts', ({ request }) => {
    const url = new URL(request.url)
    const subject = url.searchParams.get('subject')
    const items = CATALOG.filter(c => !subject || c.subject === subject).map(c => ({
      conceptId: c.conceptId,
      name: c.name,
      subject: c.subject,
      topic: c.topic,
      difficulty: c.difficulty,
      status: c.status,
    }))

    return HttpResponse.json({ items })
  }),

  http.get('/api/content/concepts/:id', ({ params }) => {
    const id = params.id as string
    const concept = CATALOG.find(c => c.conceptId === id)
    if (!concept) {
      return HttpResponse.json(
        { error: 'Concept not found' },
        { status: 404 },
      )
    }

    return HttpResponse.json({
      conceptId: concept.conceptId,
      name: concept.name,
      description: concept.description,
      subject: concept.subject,
      topic: concept.topic,
      difficulty: concept.difficulty,
      status: concept.status,
      currentMastery: concept.currentMastery,
      prerequisites: concept.prerequisites,
      dependencies: dependenciesOf(concept.conceptId),
      estimatedMinutes: concept.estimatedMinutes,
      questionCount: concept.questionCount,
    })
  }),

  http.get('/api/knowledge/path', ({ request }) => {
    const url = new URL(request.url)
    const fromConceptId = url.searchParams.get('fromConceptId') || ''
    const toConceptId = url.searchParams.get('toConceptId') || ''

    // Simple BFS over prerequisite edges (reversed — we follow "unlocks" edges)
    const from = CATALOG.find(c => c.conceptId === fromConceptId)
    const to = CATALOG.find(c => c.conceptId === toConceptId)

    if (!from || !to)
      return HttpResponse.json({ error: 'Concept not found' }, { status: 404 })

    // Build adjacency: concept → prerequisites that reach it
    const visited = new Set<string>()
    const queue: { id: string, path: string[] }[] = [{ id: fromConceptId, path: [fromConceptId] }]

    while (queue.length > 0) {
      const { id, path } = queue.shift()!
      if (id === toConceptId) {
        const nodes = path.map((cid, i) => {
          const c = CATALOG.find(x => x.conceptId === cid)!

          return {
            conceptId: cid,
            name: c.name,
            stepNumber: i + 1,
            status: c.status,
          }
        })
        const edges = path.slice(0, -1).map((cid, i) => ({
          fromConceptId: cid,
          toConceptId: path[i + 1],
          relationship: 'dependency',
        }))
        const totalMinutes = path.reduce((acc, cid) => {
          const c = CATALOG.find(x => x.conceptId === cid)!

          return acc + c.estimatedMinutes
        }, 0)

        return HttpResponse.json({
          fromConceptId,
          toConceptId,
          nodes,
          edges,
          totalSteps: path.length,
          estimatedMinutes: totalMinutes,
        })
      }
      if (visited.has(id))
        continue
      visited.add(id)
      const deps = dependenciesOf(id)
      for (const d of deps)
        queue.push({ id: d, path: [...path, d] })
    }

    return HttpResponse.json({ error: 'No path found' }, { status: 404 })
  }),
]
