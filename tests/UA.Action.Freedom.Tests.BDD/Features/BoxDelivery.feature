Feature: A box, packed to delivered
    The whole road one box travels, end to end against the deployed containers: packed by a
    Loader, validated and weighed, loaded onto a vehicle's manifest, approved — which is what
    signs off the load — its declarations filed, then prepared, readied, departed and delivered.

    The point of walking it in one scenario is that every slice has its own feature already,
    and none of them proves the slices join up. The joins are where this system goes wrong: a
    box on a manifest whose convoy never published its truck list, a vehicle that departs
    without insurance, a manifest frozen with paperwork that was never handed off.

    The French logistics envelope is part of that road. France requires one per transport
    unit at the Smart Border, so filing the ELO asks for it and the Customs Worker obtains it
    asynchronously — which is why the scenario waits for it rather than asserting immediately.

    The ICS2 Entry Summary Declaration comes before the envelope, because it pairs the
    crossing against its MRN — so an ELO is refused until an ENS is accepted. Approval is not
    gated on it any more. Freedom does not submit the declaration: a Ground Officer files it in the
    EU Customs Trader Portal and the MRN is recorded here. See docs/adr/0003.

    Note what is NOT proven here: the MRN is invented, and real French customs checks it against
    ICS2 and would answer FONC-ERR-004 for one it does not recognise. The local WireMock stub
    accepts anything, so this exercises the durable path without exercising the declaration.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/manifests"

Scenario: A validated box travels on an approved manifest and is delivered
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a category exists
    And the category maps to the EU code "300490"
    And a manifest reference that is not yet used

    # Packed, filled and weighed. Validation is the trust boundary: afterwards the box is
    # frozen, and the confirmed weight is what a border check relies on.
    When I POST "/boxes" with body:
        """
        {}
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/items" on the remembered box with body:
        """
        { "description": "Blankets", "categoryId": {category}, "properties": { "size": "double" } }
        """
    Then the response status is 200
    When I POST "/boxes/{id}/validate" on the remembered box weighing 12
    Then the response status is 204

    # The manifest is the paperwork for one vehicle on one convoy, so it is opened against the
    # truck-list entry rather than created loose.
    When I POST a manifest for the insured vehicle on the remembered convoy
    Then the response status is 201
    When I put the remembered box on the insured vehicle
    Then the response status is 204
    When I GET "/boxes" on the remembered manifest
    Then the response status is 200
    And the response body is a list of 1 or more

    When I POST "propose" on the remembered manifest
    Then the response status is 204

    # The filing sheet is what a Ground Officer takes to the portal. It is deliberately
    # incomplete: the Ukrainian delivery address is not on it, because the Ground Officer enters
    # it in the portal themselves.
    When I GET the filing sheet for the remembered vehicle
    Then the response status is 200
    And the filing sheet declares a mode of transport and a gross mass

    # The item carries no code of its own, so the category supplies it (ADR 0014).
    And the filing sheet declares commodity code "300490" for "Blankets"
    And the filing sheet withholds the delivery address and says who enters it

    # Approval signs off the load and nothing else (ADR 0004, ADR 0006): no ENS is needed to approve,
    # and nothing is filed or enqueued by it, so there is no envelope afterwards.
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Confirmed"
    And the response body field "frozen" is "True"
    When I GET "/elo" on the remembered manifest
    Then the response status is 404

    # Filing is a separate act. The ELO is created from the ENS MRN, so it is refused until an ENS
    # is accepted.
    Given I am authenticated as "operator"
    When I POST "/elo/file" on the remembered vehicle's declarations
    Then the response status is 409

    # Record what came back from ICS2. Write-once, because the envelope names it.
    When I record an ICS2 declaration for the remembered vehicle
    Then the response status is 201
    When I record the same ICS2 declaration again
    Then the response status is 409
    When I GET the ICS2 declaration for the remembered vehicle
    Then the response status is 200
    And the recorded declaration is the one I filed

    # A GMR filed by hand is recorded, not submitted, and its reference is write-once too.
    When I POST "/gmr/record" on the remembered vehicle's declarations with body:
        """
        { "reference": "GMR-BDD-1" }
        """
    Then the response status is 204
    When I POST "/gmr/record" on the remembered vehicle's declarations with body:
        """
        { "reference": "GMR-BDD-2" }
        """
    Then the response status is 409

    # The compose stack runs the ELO in automatic mode, so filing it enqueues the envelope, which the
    # Customs Worker obtains off the queue a poll cycle later.
    When I POST "/elo/file" on the remembered vehicle's declarations
    Then the response status is 202
    Then within 60 seconds the remembered manifest has a French logistics envelope
    And the envelope names a declaration and is closed but not yet paired
    And the envelope names the declaration I recorded
    And the envelope's barcode document is a PDF

    # A frozen manifest still progresses — recommendations 5.2 forbids edits, not progress —
    # because these report what happened to a load the authorities already know about.
    Given I am authenticated as "operator"
    When I POST "prepare" on the remembered manifest
    Then the response status is 204
    When I POST "ready" on the remembered manifest
    Then the response status is 204
    When I POST "depart" on the remembered manifest
    Then the response status is 204
    When I POST "deliver" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Delivered"

    # The box went with it, and the envelope that let it cross is still on file afterwards.
    When I GET "/boxes" on the remembered manifest
    Then the response status is 200
    And the response body is a list of 1 or more
    Then the remembered manifest still has its French logistics envelope

Scenario: A ground officer cannot see a vehicle's border paperwork
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    When I POST a manifest for the insured vehicle on the remembered convoy
    Then the response status is 201
    Given I am authenticated as "groundofficer"
    When I GET "/elo" on the remembered manifest
    Then the response status is 403
    When I GET "/elo/document" on the remembered manifest
    Then the response status is 403

Scenario: An envelope cannot be requested on its own
    Given I am authenticated as "admin"
    When I POST "/manifests/NOSUCH/elo"
    Then the response status is 405
