Feature: Receiver registration
    A Receiver is pending until an Administrator registers it, and only a registered Receiver can be a
    box destination. Registration is narrower than writing a Receiver: the Ground Officer, who records
    the Receiver, cannot register it. The status says only that the Receiver may be sent to, never what
    kind of body it is.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/receivers/00000000-0000-4000-8000-000000000001/usage"

Scenario: A new receiver is pending
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I GET "/receivers/{id}"
    Then the response body field "status" is "Pending"

Scenario: An administrator registers a receiver
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "admin"
    When I PUT "/receivers/{id}/status" with body:
        """
        { "status": "Registered" }
        """
    Then the response status is 204
    When I GET "/receivers/{id}"
    Then the response body field "status" is "Registered"

Scenario: The ground officer cannot register a receiver
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I PUT "/receivers/{id}/status" with body:
        """
        { "status": "Registered" }
        """
    Then the response status is 403
    When I GET "/receivers/{id}"
    Then the response body field "status" is "Pending"

Scenario: A box cannot target a pending receiver
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "receiverRef": "{id}" }
        """
    Then the response status is 409

Scenario: A box can target a registered receiver
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "admin"
    When I PUT "/receivers/{id}/status" with body:
        """
        { "status": "Registered" }
        """
    Then the response status is 204
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "receiverRef": "{id}" }
        """
    Then the response status is 201

Scenario: A box cannot target a receiver that does not exist
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "receiverRef": "00000000-0000-4000-8000-0000000000aa" }
        """
    Then the response status is 422
